using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class CheckoutController(DB db) : Controller
    {
        // TODO: replace with your actual signed-in user id lookup (e.g. from claims / session)
        private string CurrentUserId => "1";

        //GET: Checkout/Index
        public IActionResult Index()
        {
            var items = db.CartItems
                .Include(ci => ci.Product)
                    .ThenInclude(p => p.Photos)
                .Where(ci => ci.UserId == CurrentUserId)
                .ToList();

            if (!items.Any())
            {
                TempData["CheckoutError"] = "Your cart is empty.";
                return RedirectToAction("Index", "Cart");
            }

            var vm = new CheckoutViewModel
            {
                Items = items.Select(ci => new CartItemViewModel
                {
                    CartItemId = ci.Id,
                    ProductId = ci.ProductId,
                    ProductName = ci.Product.Name,
                    Price = ci.Product.Price,
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
            if (!Enum.TryParse<PaymentMethod>(paymentMethod, ignoreCase: true, out var method))
            {
                TempData["CheckoutError"] = "Please select a valid payment method.";
                return RedirectToAction("Index");
            }

            var cartItems = db.CartItems
                .Include(ci => ci.Product)
                .Where(ci => ci.UserId == CurrentUserId)
                .ToList();

            if (!cartItems.Any())
            {
                TempData["CheckoutError"] = "Your cart is empty.";
                return RedirectToAction("Index", "Cart");
            }

            // Re-check stock server-side before committing the order, in case
            // it changed since the cart page was last loaded.
            foreach (var item in cartItems)
            {
                if (item.Quantity > item.Product.Stock)
                {
                    TempData["CheckoutError"] = $"{item.Product.Name} only has {item.Product.Stock} left in stock.";
                    return RedirectToAction("Index");
                }
            }

            var total = cartItems.Sum(ci => ci.Quantity * ci.Product.Price);

            // Touch 'n Go / Credit Card are treated as paid immediately (simulated success).
            // Cash starts Unpaid until an admin marks it paid and records the amount tendered.
            var paymentStatus = method == PaymentMethod.Cash ? PaymentStatus.Unpaid : PaymentStatus.Paid;

            var order = new Order
            {
                UserId = CurrentUserId,
                PaymentMethod = method,
                PaymentStatus = paymentStatus,
                Subtotal = total,
                DiscountAmount = 0,
                Total = total
            };

            foreach (var item in cartItems)
            {
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductNameSnapshot = item.Product.Name,
                    UnitPriceSnapshot = item.Product.Price,
                    Quantity = item.Quantity,
                    LineTotal = item.Quantity * item.Product.Price
                });

                // Deduct stock now that the order is committed
                item.Product.Stock -= item.Quantity;
            }

            db.Orders.Add(order);

            // Empty the cart now that its items have become an order
            db.CartItems.RemoveRange(cartItems);

            db.SaveChanges();

            return RedirectToAction("Confirmation", new { id = order.Id });
        }

        //GET: Checkout/Confirmation/{id}
        public IActionResult Confirmation(int id)
        {
            var order = db.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefault(o => o.Id == id && o.UserId == CurrentUserId);

            if (order == null) return NotFound();

            var vm = new OrderConfirmationViewModel
            {
                OrderId = order.Id,
                OrderDateTime = order.CreatedAt,
                Status = order.PaymentStatus.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                Total = order.Total,
                PaymentMethod = order.PaymentMethod.ToString(),
                Items = order.OrderItems.Select(oi => new OrderConfirmationItemViewModel
                {
                    ProductName = oi.ProductNameSnapshot,
                    UnitPrice = oi.UnitPriceSnapshot,
                    Quantity = oi.Quantity
                }).ToList()
            };

            return View(vm);
        }
    }
}