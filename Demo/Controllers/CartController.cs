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

        // POST: Cart/Increase/{id}
        // For members, id is the CartItem's own Id (a product can now appear more than
        // once in the cart with different modifier selections, so ProductId alone is no
        // longer a unique key). For guests, id is still the ProductId — the session cart
        // doesn't track modifiers.
        [HttpPost]
        public IActionResult Increase(string id)
        {
            var user = CurrentUser;

            if (user != null)
            {
                if (!int.TryParse(id, out int cartItemId)) return NotFound();

                var item = db.CartItems
                    .Include(ci => ci.Product)
                    .Include(ci => ci.SelectedModifiers).ThenInclude(m => m.ModifierOption)
                    .FirstOrDefault(ci => ci.Id == cartItemId && ci.UserId == user.Id);
                if (item == null) return NotFound();

                if (item.Quantity < item.Product.Stock)
                {
                    item.Quantity++;
                    db.SaveChanges();
                }

                var unitPrice = item.Product.Price + item.SelectedModifiers.Sum(m => m.ModifierOption.ExtraPrice);
                return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * unitPrice });
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

        // POST: Cart/Decrease/{id}  (see Increase for what id means per user type)
        [HttpPost]
        public IActionResult Decrease(string id)
        {
            var user = CurrentUser;

            if (user != null)
            {
                if (!int.TryParse(id, out int cartItemId)) return NotFound();

                var item = db.CartItems
                    .Include(ci => ci.Product)
                    .Include(ci => ci.SelectedModifiers).ThenInclude(m => m.ModifierOption)
                    .FirstOrDefault(ci => ci.Id == cartItemId && ci.UserId == user.Id);
                if (item == null) return NotFound();

                if (item.Quantity > 1)
                {
                    item.Quantity--;
                    db.SaveChanges();
                }

                var unitPrice = item.Product.Price + item.SelectedModifiers.Sum(m => m.ModifierOption.ExtraPrice);
                return Ok(new { quantity = item.Quantity, subtotal = item.Quantity * unitPrice });
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

        // POST: Cart/Remove/{id}  (see Increase for what id means per user type)
        [HttpPost]
        public IActionResult Remove(string id)
        {
            var user = CurrentUser;

            if (user != null)
            {
                if (!int.TryParse(id, out int cartItemId)) return NotFound();

                var item = db.CartItems.FirstOrDefault(ci => ci.Id == cartItemId && ci.UserId == user.Id);
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
            var product = db.Products
                .Include(p => p.ModifierGroups).ThenInclude(g => g.Options)
                .FirstOrDefault(p => p.Id == request.ProductId);
            if (product == null) return NotFound(new { message = "Product not found." });

            var quantity = request.Quantity < 1 ? 1 : request.Quantity;
            var requestedOptionIds = (request.ModifierOptionIds ?? new List<int>()).Distinct().ToList();

            // Every requested option must actually belong to one of this product's modifier groups.
            var validOptionIds = product.ModifierGroups.SelectMany(g => g.Options).Select(o => o.Id).ToHashSet();
            if (requestedOptionIds.Any(id => !validOptionIds.Contains(id)))
            {
                return BadRequest(new { message = "Invalid modifier selection." });
            }

            // Every required modifier group must have at least one selected option.
            foreach (var group in product.ModifierGroups.Where(g => g.IsRequired))
            {
                if (!group.Options.Any(o => requestedOptionIds.Contains(o.Id)))
                {
                    return BadRequest(new { message = $"Please select an option for {group.Name}." });
                }
            }

            var sortedOptionIds = requestedOptionIds.OrderBy(x => x).ToList();
            var user = CurrentUser;

            if (user != null)
            {
                // Same product with the same exact modifier selection merges into one row;
                // a different selection (e.g. Large vs Small) becomes its own cart item.
                var existingItem = db.CartItems
                    .Include(ci => ci.SelectedModifiers)
                    .Where(ci => ci.UserId == user.Id && ci.ProductId == request.ProductId)
                    .AsEnumerable()
                    .FirstOrDefault(ci => ci.SelectedModifiers
                        .Select(m => m.ModifierOptionId)
                        .OrderBy(x => x)
                        .SequenceEqual(sortedOptionIds));

                if (existingItem != null)
                {
                    if (existingItem.Quantity + quantity > product.Stock)
                    {
                        return BadRequest(new { message = "No more stock available." });
                    }
                    existingItem.Quantity += quantity;
                }
                else
                {
                    if (product.Stock < quantity)
                    {
                        return BadRequest(new { message = "Product is out of stock." });
                    }

                    var newItem = new CartItem
                    {
                        UserId = user.Id,
                        ProductId = product.Id,
                        Quantity = quantity,
                        UnitPriceSnapshot = product.Price
                    };

                    foreach (var optionId in sortedOptionIds)
                    {
                        var option = product.ModifierGroups.SelectMany(g => g.Options).First(o => o.Id == optionId);
                        newItem.SelectedModifiers.Add(new CartItemModifier
                        {
                            ModifierOptionId = optionId,
                            Quantity = quantity,
                            UnitPriceSnapshot = option.ExtraPrice
                        });
                    }

                    db.CartItems.Add(newItem);
                }

                db.SaveChanges();

                var cartItemCount = db.CartItems.Where(ci => ci.UserId == user.Id).Sum(ci => ci.Quantity);
                return Ok(new { success = true, cartItemCount });
            }
            else
            {
                // NOTE: the guest session cart only stores ProductId -> quantity, so modifier
                // selections aren't preserved for guests yet — this mirrors prior behavior.
                var cart = hp.GetCart();

                if (cart.ContainsKey(product.Id))
                {
                    if (cart[product.Id] + quantity > product.Stock)
                    {
                        return BadRequest(new { message = "No more stock available." });
                    }
                    cart[product.Id] += quantity;
                }
                else
                {
                    if (product.Stock < quantity)
                    {
                        return BadRequest(new { message = "Product is out of stock." });
                    }
                    cart[product.Id] = quantity;
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
                    .Include(ci => ci.SelectedModifiers)
                        .ThenInclude(m => m.ModifierOption)
                    .Where(ci => ci.UserId == user.Id)
                    .OrderBy(ci => ci.Id)
                    .Select(ci => new CartItemViewModel
                    {
                        CartItemId = ci.Id,
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

            var ids = sessionCart.Keys.ToList();
            var products = db.Products.Include(p => p.Photos).Where(p => ids.Contains(p.Id)).ToList();

            return sessionCart
                .Select(kv => products.FirstOrDefault(p => p.Id == kv.Key) is { } product
                    ? new CartItemViewModel
                    {
                        CartItemId = 0,
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
        public int Quantity { get; set; } = 1;
        public List<int>? ModifierOptionIds { get; set; }
    }
}