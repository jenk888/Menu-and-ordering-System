using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using Demo.Hubs;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Demo.Controllers
{
    public class CheckoutController(DB db, Helper hp, IConfiguration configuration, IHttpClientFactory httpClientFactory, IMemoryCache cache, IHubContext<OrderHub> hub) : Controller
    {
        // Malaysian mobile numbers, local format (no +60 needed), digits only:
        //   011-XXXXXXXX  → "011" + 8 digits  (11 digits total)
        //   01X-XXXXXXX   → "01" + any digit other than 1 + 7 digits (10 digits total)
        private static readonly Regex GuestPhonePattern = new(@"^01(1\d{8}|[02-9]\d{7})$", RegexOptions.Compiled);

        // Deliberately simple - just enough to catch obvious typos, not full RFC 5322.
        private static readonly Regex GuestEmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        private User? CurrentUser =>
            User.Identity?.IsAuthenticated == true
                ? db.Users.FirstOrDefault(u => u.Email == User.Identity!.Name)
                : null;

        //GET: Checkout/Index
        public IActionResult Index()
        {
            var user = CurrentUser;
            var items = GetCartItems(user);

            if (!items.Any())
            {
                TempData["CheckoutError"] = "Your cart is empty.";
                return RedirectToAction("Index", "Cart");
            }

            var vm = new CheckoutViewModel
            {
                Items = items,
                IsGuest = user == null,
                AvailableVouchers = user != null ? GetAvailableVouchers(user) : []
            };

            return View(vm);
        }

        // POST: Checkout/PlaceOrder
        [HttpPost]
        public async Task<IActionResult> PlaceOrder(string paymentMethod, string? guestName, string? guestPhone, string? guestEmail, int? voucherId)
        {
            if (!Enum.TryParse<PaymentMethod>(paymentMethod, ignoreCase: true, out var method))
            {
                TempData["CheckoutError"] = "Please select a valid payment method.";
                return RedirectToAction("Index");
            }

            var user = CurrentUser;

            if (user == null)
            {
                if (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestPhone) || string.IsNullOrWhiteSpace(guestEmail))
                {
                    TempData["CheckoutError"] = "Please enter your name, phone number, and email.";
                    return RedirectToAction("Index");
                }

                if (!GuestPhonePattern.IsMatch(guestPhone.Trim()))
                {
                    TempData["CheckoutError"] = "Please enter a valid Malaysian mobile number (e.g. 0123456789 or 01123456789).";
                    return RedirectToAction("Index");
                }

                if (!GuestEmailPattern.IsMatch(guestEmail.Trim()))
                {
                    TempData["CheckoutError"] = "Please enter a valid email address.";
                    return RedirectToAction("Index");
                }

                voucherId = null;
            }

            var cartItems = GetCartItems(user);
            if (!cartItems.Any())
            {
                TempData["CheckoutError"] = "Your cart is empty.";
                return RedirectToAction("Index", "Cart");
            }

            foreach (var item in cartItems)
            {
                var product = db.Products.Find(item.ProductId);
                if (product == null || item.Quantity > product.Stock)
                {
                    TempData["CheckoutError"] = $"{item.ProductName} only has {product?.Stock ?? 0} left in stock.";
                    return RedirectToAction("Index");
                }
            }

            // FIX: Calculate subtotal using UnitPrice (Product Price + Modifiers Extra Price)
            var subtotal = cartItems.Sum(ci => ci.Quantity * ci.UnitPrice);

            Voucher? voucher = null;
            var discount = 0m;

            if (voucherId.HasValue)
            {
                voucher = db.Vouchers
                    .Include(v => v.VoucherRule)
                    .FirstOrDefault(v => v.Id == voucherId.Value && v.UserId == user!.Id);

                if (voucher == null || voucher.Status != VoucherStatus.Available)
                {
                    TempData["CheckoutError"] = "That voucher is no longer available.";
                    return RedirectToAction("Index");
                }

                if (subtotal < voucher.VoucherRule.MinimumSpend)
                {
                    TempData["CheckoutError"] = $"Spend at least RM {voucher.VoucherRule.MinimumSpend:0.00} to use this voucher.";
                    return RedirectToAction("Index");
                }

                discount = Math.Min(voucher.VoucherRule.DiscountAmount, subtotal);
            }

            var sst = Math.Round(subtotal * 0.06m, 2);
            var total = subtotal + sst - discount;

            // Every order starts Unpaid. Cash gets marked Paid manually at pickup.
            // FPX/Touch 'n Go get flipped to Paid by the HitPay webhook once payment
            // actually completes - we never mark it Paid just because the order was
            // placed, since at this point nothing has actually been charged yet.
            var order = new Order
            {
                UserId = user?.Id,
                GuestName = user == null ? guestName : null,
                GuestPhone = user == null ? guestPhone : null,
                // Dine-in table, if this browsing session scanned a table QR
                // beforehand (see TableController). Null for takeaway/no scan.
                TableNumber = HttpContext.Session.Get<int?>("TableNumber"),
                PaymentMethod = method,
                PaymentStatus = PaymentStatus.Unpaid,
                Subtotal = subtotal,
                DiscountAmount = discount,
                VoucherId = voucher?.Id,
                Total = total,
                CreatedAt = DateTime.UtcNow.ToMalaysiaTime()
            };

            foreach (var item in cartItems)
            {
                var product = db.Products.Find(item.ProductId)!;

                var orderItem = new OrderItem
                {
                    ProductId = product.Id,
                    ProductNameSnapshot = product.Name,
                    UnitPriceSnapshot = item.UnitPrice,
                    Quantity = item.Quantity,
                    LineTotal = item.LineTotal
                };

                // FIX: Save selected modifiers to OrderItemModifiers snapshot table
                foreach (var mod in item.SelectedModifiers)
                {
                    orderItem.SelectedModifiers.Add(new OrderItemModifier
                    {
                        ModifierGroupNameSnapshot = mod.Name, // or Group Name if tracked
                        ModifierOptionNameSnapshot = mod.Name,
                        ExtraPriceSnapshot = mod.ExtraPrice
                    });
                }

                order.OrderItems.Add(orderItem);
                product.Stock -= item.Quantity;
            }

            if (voucher != null)
            {
                voucher.UsedAt = DateTime.UtcNow;
            }

            db.Orders.Add(order);

            // Wrapped in a transaction: for online payment methods we need the real,
            // database-generated order.Id before we can ask HitPay to create a payment
            // session (it becomes the reference_number HitPay hands back on the webhook).
            // If HitPay initiation fails, we roll back the order and the stock
            // deduction above, rather than leaving an unpayable "ghost" order behind.
            using var transaction = await db.Database.BeginTransactionAsync();

            if (method == PaymentMethod.Cash)
            {
                if (user != null)
                    db.CartItems.RemoveRange(db.CartItems.Where(ci => ci.UserId == user.Id));
                else
                    hp.SetCart(null);

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Let the admin Manage page (if open) know a new order just came in.
                await hub.Clients.All.SendAsync("OrderPlaced", order.Id);

                return RedirectToAction("Confirmation", new { id = order.Id });
            }

            // Save now so order.Id exists for the reference_number below.
            await db.SaveChangesAsync();

            if (user == null)
            {
                cache.Set($"guest-email-order-{order.Id}", guestEmail!, TimeSpan.FromHours(2));
            }

            var email = user?.Email ?? guestEmail!;
            var name = user?.Name ?? guestName!;

            var (success, hitPayRedirectUrl, error) = await InitiateHitPayPaymentAsync(order, email, name);

            if (!success)
            {
                await transaction.RollbackAsync();
                TempData["CheckoutError"] = $"Payment initiation failed: {error}";
                return RedirectToAction("Index");
            }

            if (user != null)
                db.CartItems.RemoveRange(db.CartItems.Where(ci => ci.UserId == user.Id));
            else
                hp.SetCart(null);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Redirect(hitPayRedirectUrl);
        }

        // Creates a HitPay payment request for an already-saved order and returns the
        // hosted payment page URL to redirect the browser to. reference_number is the
        // real Order.Id, so the webhook can look the order back up and mark it Paid.
        private async Task<(bool Success, string RedirectUrl, string Error)> InitiateHitPayPaymentAsync(Order order, string email, string name)
        {
            var apiKey = configuration["HitPay:ApiKey"];
            var baseUrl = configuration["HitPay:BaseUrl"];
            var redirectUrl = configuration["HitPay:RedirectUrl"];
            var webhookUrl = configuration["HitPay:WebhookUrl"];

            var methodCode = order.PaymentMethod == PaymentMethod.TouchNGo ? "touch_n_go" : "card";

            var formData = new Dictionary<string, string>
            {
                { "amount", order.Total.ToString("0.00") },
                { "currency", "MYR" },
                { "email", email },
                { "name", name },
                { "reference_number", order.Id.ToString() },
                { "redirect_url", redirectUrl ?? string.Empty },
                { "webhook", webhookUrl ?? string.Empty },
                { "payment_methods[0]", methodCode }
            };

            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-BUSINESS-API-KEY", apiKey);

            var response = await client.PostAsync($"{baseUrl}/payment-requests", new FormUrlEncodedContent(formData));
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (false, string.Empty, responseString);
            }

            var result = JsonConvert.DeserializeObject<HitPayIntegration.Models.HitPayCreatePaymentResponse>(responseString);

            if (result == null || string.IsNullOrEmpty(result.url))
            {
                return (false, string.Empty, "HitPay did not return a payment URL.");
            }

            return (true, result.url, string.Empty);
        }

        private List<CartItemViewModel> GetCartItems(User? user)
        {
            if (user != null)
            {
                return db.CartItems
                    .Include(ci => ci.Product)
                        .ThenInclude(p => p.Photos)
                    .Include(ci => ci.SelectedModifiers)
                        .ThenInclude(m => m.ModifierOption)
                    .Where(ci => ci.UserId == user.Id)
                    .Select(ci => new CartItemViewModel
                    {
                        ProductId = ci.ProductId,
                        ProductName = ci.Product.Name,
                        Price = ci.Product.Price,
                        Quantity = ci.Quantity,
                        Stock = ci.Product.Stock,
                        ImageUrl = ci.Product.Photos.FirstOrDefault() != null ? ci.Product.Photos.First().PhotoUrl : null,
                        SelectedModifiers = ci.SelectedModifiers.Select(m => new CartItemModifierViewModel
                        {
                            Name = m.ModifierOption.Name,
                            ExtraPrice = m.ModifierOption.ExtraPrice
                        }).ToList()
                    })
                    .ToList();
            }

            var sessionCart = hp.GetCart();
            if (sessionCart.Count == 0) return [];

            var productIds = sessionCart.Values.Select(l => l.ProductId).Distinct().ToList();
            var products = db.Products
                .Include(p => p.Photos)
                .Include(p => p.ModifierGroups).ThenInclude(g => g.Options)
                .Where(p => productIds.Contains(p.Id))
                .ToList();

            return sessionCart
                .Select(kv => products.FirstOrDefault(p => p.Id == kv.Value.ProductId) is { } product
                    ? new CartItemViewModel
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Price = product.Price,
                        Quantity = kv.Value.Quantity,
                        Stock = product.Stock,
                        ImageUrl = product.Photos.FirstOrDefault()?.PhotoUrl,
                        SelectedModifiers = kv.Value.ModifierOptionIds
                            .Select(id => product.ModifierGroups.SelectMany(g => g.Options).FirstOrDefault(o => o.Id == id))
                            .Where(o => o != null)
                            .Select(o => new CartItemModifierViewModel { Name = o!.Name, ExtraPrice = o.ExtraPrice })
                            .ToList()
                    }
                    : null)
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();
        }

        //GET: Checkout/Confirmation/{id}
        public IActionResult Confirmation(int id)
        {
            var user = CurrentUser;

            var order = db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Voucher)
                .FirstOrDefault(o => o.Id == id && (user != null ? o.UserId == user.Id : o.UserId == null));

            if (order == null) return NotFound();

            var vm = new OrderConfirmationViewModel
            {
                OrderId = order.Id,
                OrderDateTime = order.CreatedAt,
                PaymentMethod = order.PaymentMethod.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                Subtotal = order.Subtotal,
                SST = order.Total - order.Subtotal + order.DiscountAmount,
                DiscountAmount = order.DiscountAmount,
                VoucherCode = order.Voucher?.Code,
                Total = order.Total,
                Items = order.OrderItems.Select(oi => new OrderConfirmationItemViewModel
                {
                    ProductName = oi.ProductNameSnapshot,
                    UnitPrice = oi.UnitPriceSnapshot,
                    Quantity = oi.Quantity
                }).ToList()
            };

            return View(vm);
        }

        // Vouchers this user currently holds that are neither used nor expired.
        // (Voucher.Status is [NotMapped], so the Available/Used/Expired split is
        // reproduced here directly against UsedAt/ExpiresAt so it can run in SQL.)
        private List<VoucherOptionViewModel> GetAvailableVouchers(User user)
        {
            var now = DateTime.UtcNow;

            return db.Vouchers
                .Where(v => v.UserId == user.Id && v.UsedAt == null && (v.ExpiresAt == null || v.ExpiresAt > now))
                .OrderBy(v => v.ExpiresAt ?? DateTime.MaxValue)
                .Select(v => new VoucherOptionViewModel
                {
                    Id = v.Id,
                    Code = v.Code,
                    DiscountAmount = v.VoucherRule.DiscountAmount,
                    MinimumSpend = v.VoucherRule.MinimumSpend,
                    ExpiresAt = v.ExpiresAt
                })
                .ToList();
        }
    }
}