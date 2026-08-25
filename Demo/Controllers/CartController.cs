using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class CartController(DB db) : Controller
    {
        // TODO: replace with your actual signed-in user id lookup (e.g. from claims / session)
        private string CurrentUserId => "U0001";

        //GET: Cart/Index
        public IActionResult Index()
        {
            var cart = db.Carts
                .Include(c => c.CartItems)
                    .ThenInclude(ci => ci.Product)
                        .ThenInclude(p => p.Photos)
                .FirstOrDefault(c => c.UserId == CurrentUserId);

            var vm = new CartViewModel
            {
                Items = cart?.CartItems.Select(ci => new CartItemViewModel
                {
                    CartItemId = ci.Id,
                    ProductId = ci.ProductId,
                    ProductName = ci.Product.Name,
                    Price = ci.Product.UnitPrice,
                    Quantity = ci.Quantity,
                    Stock = ci.Product.Stock,
                    ImageUrl = ci.Product.Photos.FirstOrDefault()?.PhotoUrl
                }).ToList() ?? []
            };

            return View(vm);
        }

        //POST: Cart/Increase/{id}
        [HttpPost]
        public IActionResult Increase(string id)
        {
            var item = db.CartItems.Include(ci => ci.Product).FirstOrDefault(ci => ci.Id == id);
            if (item == null) return NotFound();

            if (item.Quantity < item.Product.Stock)
            {
                item.Quantity++;
                db.SaveChanges();
            }

            return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * item.Product.UnitPrice });
        }

        //POST: Cart/Decrease/{id}
        [HttpPost]
        public IActionResult Decrease(string id)
        {
            var item = db.CartItems.Include(ci => ci.Product).FirstOrDefault(ci => ci.Id == id);
            if (item == null) return NotFound();

            if (item.Quantity > 1)
            {
                item.Quantity--;
                db.SaveChanges();
            }

            return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * item.Product.UnitPrice });
        }

        //POST: Cart/Remove/{id}
        [HttpPost]
        public IActionResult Remove(string id)
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
            if (product == null) return NotFound();

            var cart = db.Carts
                .Include(c => c.CartItems)
                .FirstOrDefault(c => c.UserId == CurrentUserId);

            if (cart == null)
            {
                cart = new Cart { Id = GenerateId(db.Carts.Select(c => c.Id)), UserId = CurrentUserId };
                db.Carts.Add(cart);
            }

            var existingItem = cart.CartItems.FirstOrDefault(ci => ci.ProductId == request.ProductId);
            if (existingItem != null)
            {
                if (existingItem.Quantity >= product.Stock) return BadRequest(new { message = "No more stock available." });
                existingItem.Quantity++;
            }
            else
            {
                if (product.Stock <= 0) return BadRequest(new { message = "Product is out of stock." });
                cart.CartItems.Add(new CartItem
                {
                    Id = GenerateId(db.CartItems.Select(ci => ci.Id)),
                    ProductId = request.ProductId,
                    Quantity = 1
                });
            }

            db.SaveChanges();
            return Ok();
        }

        // Generates a random 5-character ID and retries on the rare collision.
        // TODO: consider switching these keys to int identity columns if you don't need short display IDs.
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

    public class AddToCartRequest
    {
        public string ProductId { get; set; } = string.Empty;
    }
}
