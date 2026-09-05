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
            var vm = new CartViewModel { Items = GetCartItems() };
            return View(vm);
        }

        //POST: Cart/Increase/{id}
        [HttpPost]
        public IActionResult Increase(string id)
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
                if (!cart.ContainsKey(id)) return NotFound();

                if (cart[id] < product.Stock)
                {
                    cart[id]++;
                    hp.SetCart(cart);
                }

                return Ok(new { quantity = cart[id], subtotal = cart[id] * product.Price });
            }
        }

        //POST: Cart/Decrease/{id}
        [HttpPost]
        public IActionResult Decrease(string id)
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
                if (!cart.ContainsKey(id)) return NotFound();

                if (cart[id] > 1)
                {
                    cart[id]--;
                    hp.SetCart(cart);
                }

                return Ok(new { quantity = cart[id], subtotal = cart[id] * product.Price });
            }
        }

        //POST: Cart/Remove/{id}
        [HttpPost]
        public IActionResult Remove(string id)
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
                if (!cart.Remove(id)) return NotFound();
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

                if (cart.ContainsKey(product.Id))
                {
                    if (cart[product.Id] >= product.Stock) return BadRequest(new { message = "No more stock available." });
                    cart[product.Id]++;
                }
                else
                {
                    if (product.Stock <= 0) return BadRequest(new { message = "Product is out of stock." });
                    cart[product.Id] = 1;
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

    public class AddToCartRequest
    {
        public string ProductId { get; set; } = null!;
    }
}