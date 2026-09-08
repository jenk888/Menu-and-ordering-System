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

        // Sum of a guest cart line's selected modifiers' extra price.
        private static decimal GuestLineExtraPrice(Product product, GuestCartLine line)
        {
            var options = product.ModifierGroups.SelectMany(g => g.Options).ToDictionary(o => o.Id);
            return line.ModifierOptionIds.Sum(id => options.TryGetValue(id, out var o) ? o.ExtraPrice : 0);
        }

        // POST: Cart/Increase/{id}
        // For members, id is the CartItem's own Id. For guests, id is the composite
        // GuestCartLine key (product + modifier selection) — see GuestCartLine.MakeKey.
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
                var cart = hp.GetCart();
                if (!cart.TryGetValue(id, out var line)) return NotFound();

                var product = db.Products
                    .Include(p => p.ModifierGroups).ThenInclude(g => g.Options)
                    .FirstOrDefault(p => p.Id == line.ProductId);
                if (product == null) return NotFound();

                if (line.Quantity < product.Stock)
                {
                    line.Quantity++;
                    hp.SetCart(cart);
                }

                var unitPrice = product.Price + GuestLineExtraPrice(product, line);
                return Ok(new { quantity = line.Quantity, subtotal = line.Quantity * unitPrice });
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
                var cart = hp.GetCart();
                if (!cart.TryGetValue(id, out var line)) return NotFound();

                var product = db.Products
                    .Include(p => p.ModifierGroups).ThenInclude(g => g.Options)
                    .FirstOrDefault(p => p.Id == line.ProductId);
                if (product == null) return NotFound();

                if (line.Quantity > 1)
                {
                    line.Quantity--;
                    hp.SetCart(cart);
                }

                var unitPrice = product.Price + GuestLineExtraPrice(product, line);
                return Ok(new { quantity = line.Quantity, subtotal = line.Quantity * unitPrice });
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

            var validOptionIds = product.ModifierGroups.SelectMany(g => g.Options).Select(o => o.Id).ToHashSet();
            if (requestedOptionIds.Any(id => !validOptionIds.Contains(id)))
            {
                return BadRequest(new { message = "Invalid modifier selection." });
            }

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
                TempData["Info"] = "Item added successfully";
                return Ok(new { success = true, cartItemCount });
            }
            else
            {
                var cart = hp.GetCart();
                var key = GuestCartLine.MakeKey(product.Id, sortedOptionIds);

                if (cart.TryGetValue(key, out var existingLine))
                {
                    if (existingLine.Quantity + quantity > product.Stock)
                    {
                        return BadRequest(new { message = "No more stock available." });
                    }
                    existingLine.Quantity += quantity;
                }
                else
                {
                    if (product.Stock < quantity)
                    {
                        return BadRequest(new { message = "Product is out of stock." });
                    }

                    cart[key] = new GuestCartLine
                    {
                        ProductId = product.Id,
                        Quantity = quantity,
                        ModifierOptionIds = sortedOptionIds
                    };
                }

                hp.SetCart(cart);

                TempData["Info"] = "Item added successfully";
                return Ok(new { success = true, cartItemCount = cart.Values.Sum(l => l.Quantity) });
            }
        }

        // POST: Cart/UpdateItem
        // Edits an existing line's quantity/modifiers, for both members and guests now.
        // request.Id is the CartItem.Id (member) or the current composite guest key (guest).
        [HttpPost]
        public IActionResult UpdateItem([FromBody] UpdateCartItemRequest request)
        {
            var product = db.Products
                .Include(p => p.ModifierGroups).ThenInclude(g => g.Options)
                .FirstOrDefault(p => p.Id == request.ProductId);
            if (product == null) return NotFound(new { message = "Product not found." });

            var quantity = request.Quantity < 1 ? 1 : request.Quantity;
            var requestedOptionIds = (request.ModifierOptionIds ?? new List<int>()).Distinct().ToList();

            var validOptionIds = product.ModifierGroups.SelectMany(g => g.Options).Select(o => o.Id).ToHashSet();
            if (requestedOptionIds.Any(id => !validOptionIds.Contains(id)))
            {
                return BadRequest(new { message = "Invalid modifier selection." });
            }

            foreach (var group in product.ModifierGroups.Where(g => g.IsRequired))
            {
                if (!group.Options.Any(o => requestedOptionIds.Contains(o.Id)))
                {
                    return BadRequest(new { message = $"Please select an option for {group.Name}." });
                }
            }

            if (product.Stock < quantity)
            {
                return BadRequest(new { message = "Not enough stock available." });
            }

            var sortedOptionIds = requestedOptionIds.OrderBy(x => x).ToList();
            var user = CurrentUser;

            if (user != null)
            {
                if (!int.TryParse(request.Id, out int cartItemId)) return NotFound();

                var item = db.CartItems
                    .Include(ci => ci.SelectedModifiers)
                    .FirstOrDefault(ci => ci.Id == cartItemId && ci.UserId == user.Id);
                if (item == null) return NotFound();

                var duplicate = db.CartItems
                    .Include(ci => ci.SelectedModifiers)
                    .Where(ci => ci.UserId == user.Id && ci.ProductId == item.ProductId && ci.Id != item.Id)
                    .AsEnumerable()
                    .FirstOrDefault(ci => ci.SelectedModifiers
                        .Select(m => m.ModifierOptionId)
                        .OrderBy(x => x)
                        .SequenceEqual(sortedOptionIds));

                if (duplicate != null)
                {
                    if (duplicate.Quantity + quantity > product.Stock)
                    {
                        return BadRequest(new { message = "No more stock available." });
                    }
                    duplicate.Quantity += quantity;
                    db.CartItems.Remove(item);
                }
                else
                {
                    item.Quantity = quantity;

                    db.CartItemModifiers.RemoveRange(item.SelectedModifiers);
                    item.SelectedModifiers.Clear();

                    foreach (var optionId in sortedOptionIds)
                    {
                        var option = product.ModifierGroups.SelectMany(g => g.Options).First(o => o.Id == optionId);
                        item.SelectedModifiers.Add(new CartItemModifier
                        {
                            ModifierOptionId = optionId,
                            Quantity = quantity,
                            UnitPriceSnapshot = option.ExtraPrice
                        });
                    }
                }

                db.SaveChanges();

                var cartItemCount = db.CartItems.Where(ci => ci.UserId == user.Id).Sum(ci => ci.Quantity);
                TempData["Info"] = "Cart updated successfully";
                return Ok(new { success = true, cartItemCount });
            }
            else
            {
                var cart = hp.GetCart();
                if (!cart.ContainsKey(request.Id)) return NotFound();

                var newKey = GuestCartLine.MakeKey(product.Id, sortedOptionIds);

                // If the new selection now matches a different existing line for this
                // product, fold into that line instead of leaving two lines behind.
                if (newKey != request.Id && cart.TryGetValue(newKey, out var duplicateLine))
                {
                    if (duplicateLine.Quantity + quantity > product.Stock)
                    {
                        return BadRequest(new { message = "No more stock available." });
                    }
                    duplicateLine.Quantity += quantity;
                    cart.Remove(request.Id);
                }
                else
                {
                    cart.Remove(request.Id);
                    cart[newKey] = new GuestCartLine
                    {
                        ProductId = product.Id,
                        Quantity = quantity,
                        ModifierOptionIds = sortedOptionIds
                    };
                }

                hp.SetCart(cart);

                TempData["Info"] = "Cart updated successfully";
                return Ok(new { success = true, cartItemCount = cart.Values.Sum(l => l.Quantity) });
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
                        CartItemId = 0,
                        GuestLineKey = kv.Key,
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
    }

    public class AddToCartRequest
    {
        public string ProductId { get; set; } = null!;
        public int Quantity { get; set; } = 1;
        public List<int>? ModifierOptionIds { get; set; }
    }

    public class UpdateCartItemRequest
    {
        public string Id { get; set; } = null!;
        public string ProductId { get; set; } = null!;
        public int Quantity { get; set; } = 1;
        public List<int>? ModifierOptionIds { get; set; }
    }
}