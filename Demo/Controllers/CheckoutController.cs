using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class CheckoutController(DB db, Helper hp) : Controller
    {
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
                IsGuest = user == null
            };

            return View(vm);
        }

        //POST: Checkout/PlaceOrder
        [HttpPost]
        public IActionResult PlaceOrder(string paymentMethod, string? guestName, string? guestPhone)
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

            var subtotal = cartItems.Sum(ci => ci.Quantity * ci.Price);
            var sst = Math.Round(subtotal * 0.06m, 2);
            var total = subtotal + sst;

            var paymentStatus = method == PaymentMethod.Cash ? PaymentStatus.Unpaid : PaymentStatus.Paid;

            var order = new Order
            {
                UserId = user?.Id,
                GuestName = user == null ? guestName : null,
                GuestPhone = user == null ? guestPhone : null,
                PaymentMethod = method,
                PaymentStatus = paymentStatus,
                Subtotal = subtotal,
                DiscountAmount = 0,
                Total = total,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var item in cartItems)
            {
                var product = db.Products.Find(item.ProductId)!;

                order.OrderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    ProductNameSnapshot = product.Name,
                    UnitPriceSnapshot = product.Price,
                    Quantity = item.Quantity,
                    LineTotal = item.Quantity * product.Price
                });

                product.Stock -= item.Quantity;
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

        //GET: Checkout/Confirmation/{id}
        public IActionResult Confirmation(int id)
        {
            var user = CurrentUser;

            var order = db.Orders
                .Include(o => o.OrderItems)
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

        private List<CartItemViewModel> GetCartItems(User? user)
        {
            if (user != null)
            {
                return db.CartItems
                    .Include(ci => ci.Product)
                        .ThenInclude(p => p.Photos)
                    .Where(ci => ci.UserId == user.Id)
                    .Select(ci => new CartItemViewModel
                    {
                        ProductId = ci.ProductId,
                        ProductName = ci.Product.Name,
                        Price = ci.Product.Price,
                        Quantity = ci.Quantity,
                        Stock = ci.Product.Stock,
                        ImageUrl = ci.Product.Photos.FirstOrDefault() != null ? ci.Product.Photos.First().PhotoUrl : null
                    })
                    .ToList();
            }

            var sessionCart = hp.GetCart();
            if (sessionCart.Count == 0) return [];

            var ids = sessionCart.Keys.ToList();
            var products = db.Products.Include(p => p.Photos).Where(p => ids.Contains(p.Id)).ToList();

            return sessionCart
                .Select(kv => products.FirstOrDefault(p => p.Id == kv.Key) is { } product
                    ? new CartItemViewModel
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Price = product.Price,
                        Quantity = kv.Value,
                        Stock = product.Stock,
                        ImageUrl = product.Photos.FirstOrDefault()?.PhotoUrl
                    }
                    : null)
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();
        }
    }
}