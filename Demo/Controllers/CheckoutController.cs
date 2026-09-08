using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using System.Text.RegularExpressions;

namespace Demo.Controllers
{
    public class CheckoutController(DB db, Helper hp) : Controller
    {
        // Malaysian mobile numbers, local format (no +60 needed), digits only:
        //   011-XXXXXXXX  → "011" + 8 digits  (11 digits total)
        //   01X-XXXXXXX   → "01" + any digit other than 1 + 7 digits (10 digits total)
        private static readonly Regex GuestPhonePattern = new(@"^01(1\d{8}|[02-9]\d{7})$", RegexOptions.Compiled);

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
        public IActionResult PlaceOrder(string paymentMethod, string? guestName, string? guestPhone, int? voucherId)
        {
            if (!Enum.TryParse<PaymentMethod>(paymentMethod, ignoreCase: true, out var method))
            {
                TempData["CheckoutError"] = "Please select a valid payment method.";
                return RedirectToAction("Index");
            }

            var user = CurrentUser;

            if (user == null)
            {
                if (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestPhone))
                {
                    TempData["CheckoutError"] = "Please enter your name and phone number.";
                    return RedirectToAction("Index");
                }

                if (!GuestPhonePattern.IsMatch(guestPhone.Trim()))
                {
                    TempData["CheckoutError"] = "Please enter a valid Malaysian mobile number (e.g. 0123456789 or 01123456789).";
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

            var paymentStatus = method == PaymentMethod.Cash ? PaymentStatus.Unpaid : PaymentStatus.Paid;

            var order = new Order
            {
                UserId = user?.Id,
                GuestName = user == null ? guestName : null,
                GuestPhone = user == null ? guestPhone : null,
                PaymentMethod = method,
                PaymentStatus = paymentStatus,
                Subtotal = subtotal,
                DiscountAmount = discount,
                VoucherId = voucher?.Id,
                Total = total,
                CreatedAt = DateTime.UtcNow
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

            if (user != null)
            {
                db.CartItems.RemoveRange(db.CartItems.Where(ci => ci.UserId == user.Id));
            }
            else
            {
                hp.SetCart(null);
            }

            db.SaveChanges();

            return RedirectToAction("Confirmation", new { id = order.Id });
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