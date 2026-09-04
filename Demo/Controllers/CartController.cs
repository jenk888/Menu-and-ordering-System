using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class CartController(DB db) : Controller
    {
        // TODO: replace with your actual signed-in user id lookup (e.g. from claims / session)
        private string CurrentUserId => "1";

        //GET: Cart/Index
        public IActionResult Index()
        {
            var items = db.CartItems
                .Include(ci => ci.Product)
                    .ThenInclude(p => p.Photos)
                .Where(ci => ci.UserId == CurrentUserId)
                .ToList();

            var vm = new CartViewModel
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

        //POST: Cart/Increase/{id}
        [HttpPost]
        public IActionResult Increase(int id)
        {
            var item = db.CartItems.Include(ci => ci.Product).FirstOrDefault(ci => ci.Id == id);
            if (item == null) return NotFound();

            if (item.Quantity < item.Product.Stock)
            {
                item.Quantity++;
                db.SaveChanges();
            }

            return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * item.Product.Price });
        }

        //POST: Cart/Decrease/{id}
        [HttpPost]
        public IActionResult Decrease(int id)
        {
            var item = db.CartItems.Include(ci => ci.Product).FirstOrDefault(ci => ci.Id == id);
            if (item == null) return NotFound();

            if (item.Quantity > 1)
            {
                item.Quantity--;
                db.SaveChanges();
            }

            return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * item.Product.Price });
        }

        //POST: Cart/Remove/{id}
        [HttpPost]
        public IActionResult Remove(int id)
        {
            var item = db.CartItems.FirstOrDefault(ci => ci.Id == id);
            if (item == null) return NotFound();

            db.CartItems.Remove(item);
            db.SaveChanges();

            return Ok();
        }

        //POST: Cart/Add
        [HttpPost]
        public IActionResult Add([FromBody] AddToCartRequest request)
        {
            var product = db.Products.Find(request.ProductId);
            if (product == null) return NotFound(new { message = "Product not found." });

            // CartItem.UserId is a required FK to Users.Id — without a real user row, saving
            // a CartItem for CurrentUserId throws at SaveChanges. Checking here gives a clear
            // message instead of a raw 500. Remove this once login is wired up properly.
            if (!db.Users.Any(u => u.Id == CurrentUserId))
            {
                return BadRequest(new { message = $"No user with Id '{CurrentUserId}' exists yet. Insert a User row with this Id (or wire up real authentication) before testing Add to Cart." });
            }

            var existingItem = db.CartItems
                .Include(ci => ci.Product)
                .FirstOrDefault(ci => ci.UserId == CurrentUserId && ci.ProductId == request.ProductId);

            if (existingItem != null)
            {
                if (existingItem.Quantity >= product.Stock) return BadRequest(new { message = "No more stock available." });
                existingItem.Quantity++;
            }
            else
            {
                if (product.Stock <= 0) return BadRequest(new { message = "Product is out of stock." });
                db.CartItems.Add(new CartItem
                {
                    UserId = CurrentUserId,
                    ProductId = request.ProductId,
                    Quantity = 1,
                    UnitPriceSnapshot = product.Price
                });
            }

            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                // Surfaces the real DB error (e.g. FK violation) instead of a bare 500,
                // so the browser console / network tab shows what actually failed.
                return StatusCode(500, new { message = "Database error while saving the cart.", detail = ex.InnerException?.Message ?? ex.Message });
            }

            var cartItemCount = db.CartItems.Where(ci => ci.UserId == CurrentUserId).Sum(ci => ci.Quantity);
            return Ok(new { success = true, cartItemCount });
        }
    }

    public class AddToCartRequest
    {
        public string ProductId { get; set; }
    }
}
