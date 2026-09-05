using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class CartController(DB db, Helper hp) : Controller
    {
        private User? CurrentUser =>
            User.Identity?.IsAuthenticated == true
                ? db.Users.FirstOrDefault(u => u.Email == User.Identity!.Name)
                : null;

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
            var user = CurrentUser;

            if (user != null)
            {
                var item = db.CartItems.Include(ci => ci.Product)
                    .FirstOrDefault(ci => ci.UserId == user.Id && ci.ProductId == id);
                if (item == null) return NotFound();

                if (item.Quantity < item.Product.Stock)
                {
                    item.Quantity++;
                    db.SaveChanges();
                }

                return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * item.Product.Price });
            }
            else
            {
                var product = db.Products.Find(id);
                if (product == null) return NotFound();

                var cart = hp.GetCart();
                var key = id.ToString();
                if (!cart.ContainsKey(key)) return NotFound();

                if (cart[key] < product.Stock)
                {
                    cart[key]++;
                    hp.SetCart(cart);
                }

                return Ok(new { quantity = cart[key], subtotal = cart[key] * product.Price });
            }
        }

        //POST: Cart/Decrease/{id}
        [HttpPost]
        public IActionResult Decrease(int id)
        {
            var user = CurrentUser;

            if (user != null)
            {
                var item = db.CartItems.Include(ci => ci.Product)
                    .FirstOrDefault(ci => ci.UserId == user.Id && ci.ProductId == id);
                if (item == null) return NotFound();

                if (item.Quantity > 1)
                {
                    item.Quantity--;
                    db.SaveChanges();
                }

                return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * item.Product.Price });
            }
            else
            {
                var product = db.Products.Find(id);
                if (product == null) return NotFound();

                var cart = hp.GetCart();
                var key = id.ToString();
                if (!cart.ContainsKey(key)) return NotFound();

                if (cart[key] > 1)
                {
                    cart[key]--;
                    hp.SetCart(cart);
                }

                return Ok(new { quantity = cart[key], subtotal = cart[key] * product.Price });
            }
        }

        //POST: Cart/Remove/{id}
        [HttpPost]
        public IActionResult Remove(int id)
        {
            var user = CurrentUser;

            if (user != null)
            {
                var item = db.CartItems.FirstOrDefault(ci => ci.UserId == user.Id && ci.ProductId == id);
                if (item == null) return NotFound();

                db.CartItems.Remove(item);
                db.SaveChanges();
            }
            else
            {
                var cart = hp.GetCart();
                if (!cart.Remove(id.ToString())) return NotFound();
                hp.SetCart(cart);
            }

            return Ok();
        }

        //POST: Cart/Add
        [HttpPost]
        public IActionResult Add([FromBody] AddToCartRequest request)
        {
            var product = db.Products.Find(request.ProductId);
            if (product == null) return NotFound(new { message = "Product not found." });

            var user = CurrentUser;

            if (user != null)
            {
                var existingItem = db.CartItems.FirstOrDefault(ci => ci.UserId == user.Id && ci.ProductId == request.ProductId);

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
                        UserId = user.Id,
                        ProductId = product.Id,
                        Quantity = 1,
                        UnitPriceSnapshot = product.Price
                    });
                }

                db.SaveChanges();

                var cartItemCount = db.CartItems.Where(ci => ci.UserId == user.Id).Sum(ci => ci.Quantity);
                return Ok(new { success = true, cartItemCount });
            }
            else
            {
                var cart = hp.GetCart();
                var key = product.Id.ToString();

                if (cart.ContainsKey(key))
                {
                    if (cart[key] >= product.Stock) return BadRequest(new { message = "No more stock available." });
                    cart[key]++;
                }
                else
                {
                    if (product.Stock <= 0) return BadRequest(new { message = "Product is out of stock." });
                    cart[key] = 1;
                }

                hp.SetCart(cart);

                return Ok(new { success = true, cartItemCount = cart.Values.Sum() });
            }
        }

        private List<CartItemViewModel> GetCartItems()
        {
            var user = CurrentUser;

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

            var ids = sessionCart.Keys.Select(int.Parse).ToList();
            var products = db.Products.Include(p => p.Photos).Where(p => ids.Contains(p.Id)).ToList();

            return sessionCart
                .Select(kv => products.FirstOrDefault(p => p.Id == int.Parse(kv.Key)) is { } product
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

    public class AddToCartRequest
    {
        public string ProductId { get; set; }
    }
}
