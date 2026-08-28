using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class CheckoutController(DB db) : Controller
    {
        // TODO: replace with your actual signed-in user id lookup (e.g. from claims / session)
        private string CurrentUserId => "U0001";

        //GET: Checkout/Index
        public IActionResult Index()
        {
            var cart = db.Carts
                .Include(c => c.CartItems)
                    .ThenInclude(ci => ci.Product)
                        .ThenInclude(p => p.Photos)
                .FirstOrDefault(c => c.UserId == CurrentUserId);

            if (cart == null || !cart.CartItems.Any())
            {
                TempData["CheckoutError"] = "Your cart is empty.";
                return RedirectToAction("Index", "Cart");
            }

            var vm = new CheckoutViewModel
            {
                Items = cart.CartItems.Select(ci => new CartItemViewModel
                {
                    CartItemId = ci.Id,
                    ProductId = ci.ProductId,
                    ProductName = ci.Product.Name,
                    Price = ci.Product.UnitPrice,
                    Quantity = ci.Quantity,
                    Stock = ci.Product.Stock,
                    ImageUrl = ci.Product.Photos.FirstOrDefault()?.PhotoUrl
                }).ToList()
            };

            return View(vm);
        }

        //POST: Checkout/PlaceOrder
        [HttpPost]
        public IActionResult PlaceOrder(string paymentMethod)
        {
            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                TempData["CheckoutError"] = "Please select a payment method.";
                return RedirectToAction("Index");
            }

            var cart = db.Carts
                .Include(c => c.CartItems)
                    .ThenInclude(ci => ci.Product)
                .FirstOrDefault(c => c.UserId == CurrentUserId);

            if (cart == null || !cart.CartItems.Any())
            {
                TempData["CheckoutError"] = "Your cart is empty.";
                return RedirectToAction("Index", "Cart");
            }

            // Re-check stock server-side before committing the order, in case
            // it changed since the cart page was last loaded.
            foreach (var item in cart.CartItems)
            {
                if (item.Quantity > item.Product.Stock)
                {
                    TempData["CheckoutError"] = $"{item.Product.Name} only has {item.Product.Stock} left in stock.";
                    return RedirectToAction("Index");
                }
            }

            var total = cart.CartItems.Sum(ci => ci.Quantity * ci.Product.UnitPrice);

            var order = new Order
            {
                Id = GenerateId(db.Orders.Select(o => o.Id)),
                OrderDateTime = DateTime.Now,
                Status = "Paid",
                TotalAmount = total,
                UserId = CurrentUserId
            };

            foreach (var item in cart.CartItems)
            {
                order.Details.Add(new OrderDetail
                {
                    Id = GenerateId(db.OrderDetails.Select(od => od.Id)),
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Product.UnitPrice
                });

                // Deduct stock now that the order is committed
                item.Product.Stock -= item.Quantity;
            }

            order.Payment = new Payment
            {
                Id = GenerateId(db.Payments.Select(p => p.Id)),
                PaymentMethod = paymentMethod,
                Amount = total,
                Status = "Completed",
                PaidDate = DateTime.Now
            };

            db.Orders.Add(order);

            // Empty the cart now that its items have become an order
            db.CartItems.RemoveRange(cart.CartItems);

            db.SaveChanges();

            return RedirectToAction("Confirmation", new { id = order.Id });
        }

        //GET: Checkout/Confirmation/{id}
        public IActionResult Confirmation(string id)
        {
            var order = db.Orders
                .Include(o => o.Details)
                    .ThenInclude(d => d.Product)
                .Include(o => o.Payment)
                .FirstOrDefault(o => o.Id == id && o.UserId == CurrentUserId);

            if (order == null) return NotFound();

            var vm = new OrderConfirmationViewModel
            {
                OrderId = order.Id,
                OrderDateTime = order.OrderDateTime,
                Status = order.Status,
                Total = order.TotalAmount,
                PaymentMethod = order.Payment?.PaymentMethod ?? "-",
                PaymentStatus = order.Payment?.Status ?? "-",
                Items = order.Details.Select(d => new OrderConfirmationItemViewModel
                {
                    ProductName = d.Product.Name,
                    UnitPrice = d.UnitPrice,
                    Quantity = d.Quantity
                }).ToList()
            };

            return View(vm);
        }

        // Generates a random 5-character ID and retries on the rare collision.
        // NOTE: duplicated from CartController for now — worth moving to a shared
        // helper class if you add more controllers that need generated IDs.
        private static string GenerateId(IQueryable<string> existingIds)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            string id;
            do
            {
                id = new string(Enumerable.Range(0, 5).Select(_ => chars[random.Next(chars.Length)]).ToArray());
            } while (existingIds.Any(x => x == id));

            return id;
        }
    }
}
