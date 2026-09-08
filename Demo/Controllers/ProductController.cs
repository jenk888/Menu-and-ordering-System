using Demo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Demo.Helper;

namespace Demo.Controllers
{
    public class ProductController(DB db, Helper hp) : Controller
    {
        private User? CurrentUser =>
            User.Identity?.IsAuthenticated == true
                ? db.Users.FirstOrDefault(u => u.Email == User.Identity!.Name)
                : null;

        // GET: Product/Index
        [Route("")]
        [Route("Product")]
        [Route("Product/Index")]
        public IActionResult Index()
        {
            ViewBag.Categories = db.Categories.ToList();

            var model = db.Products;
            return View(model);
        }

        // GET: Product/Details/{id}
        public IActionResult Details(string? id, string? editCartItemId)
        {
            if (string.IsNullOrEmpty(id))
            {
                return RedirectToAction("Index");
            }

            var p = db.Products
                .Include(x => x.Category)
                .Include(x => x.Photos)
                .Include(x => x.ModifierGroups).ThenInclude(g => g.Options)
                .FirstOrDefault(x => x.Id == id);

            if (p == null)
            {
                return RedirectToAction("Index");
            }

            // Arrived from the cart's "edit" flow. editCartItemId is the CartItem.Id (as string)
            // for members, or the composite GuestCartLine key for guests — see GuestCartLine.MakeKey.
            if (!string.IsNullOrEmpty(editCartItemId))
            {
                var user = CurrentUser;

                if (user != null)
                {
                    if (int.TryParse(editCartItemId, out int cartItemId))
                    {
                        var cartItem = db.CartItems
                            .Include(ci => ci.SelectedModifiers)
                            .FirstOrDefault(ci => ci.Id == cartItemId && ci.UserId == user.Id && ci.ProductId == id);

                        if (cartItem != null)
                        {
                            ViewBag.EditCartItem = new CartItemEditInfo
                            {
                                LineId = cartItem.Id.ToString(),
                                Quantity = cartItem.Quantity,
                                SelectedOptionIds = cartItem.SelectedModifiers.Select(m => m.ModifierOptionId).ToList()
                            };
                        }
                    }
                }
                else
                {
                    var cart = hp.GetCart();
                    if (cart.TryGetValue(editCartItemId, out var line) && line.ProductId == id)
                    {
                        ViewBag.EditCartItem = new CartItemEditInfo
                        {
                            LineId = editCartItemId,
                            Quantity = line.Quantity,
                            SelectedOptionIds = line.ModifierOptionIds
                        };
                    }
                }
            }

            return View(p);
        }

        // GET: Product/Manage
        public IActionResult Manage()
        {
            var model = db.Products
                .Include(p => p.Category)
                .Include(p => p.Photos)
                .OrderBy(p => p.Id)
                .ToList();

            return View(model);
        }

        // AJAX Endpoint for Filtering Products
        [HttpGet]
        public IActionResult GetProducts(int? categoryId = null, string? search = null, decimal? minPrice = null, decimal? maxPrice = null)
        {
            var query = db.Products
                .Where(p => p.IsAvailable);

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p => p.Name.Contains(search));
            }

            if (minPrice.HasValue)
            {
                query = query.Where(p => p.Price >= minPrice.Value);
            }


            if (maxPrice.HasValue)
            {
                query = query.Where(p => p.Price <= maxPrice.Value);
            }

            var products = query.Select(p => new
            {
                p.Id,
                p.Name,
                p.Price,
                p.Stock,
                PhotoUrl = p.Photos.Select(ph => ph.PhotoUrl).FirstOrDefault() != null
                    ? "/photos/product/" + p.Photos.Select(ph => ph.PhotoUrl).FirstOrDefault()
                    : "/photos/no-image.jpg"
            }).ToList();

            return Json(products);
        }

        // AJAX Endpoint: preview the next Product Id for a chosen category
        [HttpGet]
        public IActionResult GetNextId(int categoryId)
        {
            return Json(NextId(categoryId));
        }

        // Generates an Id like "F001" — first letter of the category's name + a 3-digit counter
        // scoped to that letter, so different categories can restart their own numbering.
        private string NextId(int categoryId)
        {
            var cat = db.Categories.Find(categoryId);
            if (cat == null || string.IsNullOrEmpty(cat.Name))
            {
                return "X001";
            }

            string prefix = char.ToUpper(cat.Name[0]).ToString();

            int max = db.Products
                .Where(p => p.Id.StartsWith(prefix))
                .Select(p => p.Id)
                .AsEnumerable()
                .Select(id => int.TryParse(id[1..], out int n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();

            return prefix + (max + 1).ToString("000");
        }

        // GET: Product/Insert
        [Authorize(Roles = "Admin")]
        public IActionResult Insert()
        {
            ViewBag.Categories = db.Categories.ToList();

            var vm = new ProductInsertViewModel
            {
                Price = 0.01m,
            };

            return View(vm);
        }

        // POST: Product/Insert
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Insert(ProductInsertViewModel vm)
        {
            if (ModelState.IsValid("CategoryId") && !db.Categories.Any(c => c.Id == vm.CategoryId))
            {
                ModelState.AddModelError("CategoryId", "Invalid category.");
            }

            // [Required] doesn't fire on an empty (non-null) list, so check explicitly.
            if (vm.Photos == null || vm.Photos.Count == 0)
            {
                ModelState.AddModelError("Photos", "Please select at least one photo.");
            }
            else if (ModelState.IsValid("Photos"))
            {
                foreach (var file in vm.Photos)
                {
                    var e = hp.ValidatePhoto(file);
                    if (e != "")
                    {
                        ModelState.AddModelError("Photos", e);
                        break;
                    }
                }
            }

            if (ModelState.IsValid)
            {
                var p = new Product
                {
                    Id = NextId(vm.CategoryId),
                    Name = vm.Name,
                    Price = vm.Price,
                    CategoryId = vm.CategoryId,
                };

                foreach (var file in vm.Photos)
                {
                    p.Photos.Add(new ProductPhoto
                    {
                        PhotoUrl = hp.SavePhoto(file, "products"),
                    });
                }

                db.Products.Add(p);
                db.SaveChanges();

                TempData["Info"] = "Product inserted.";
                return RedirectToAction("Manage");
            }

            ViewBag.Categories = db.Categories.ToList();
            return View(vm);
        }

        // GET: Product/BatchInsert
        [Authorize(Roles = "Admin")]
        public IActionResult BatchInsert()
        {
            return View();
        }

        // POST: Product/BatchInsert
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> BatchInsert(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please choose a text file.");
                return View();
            }

            var messages = new List<string>();
            int inserted = 0, skipped = 0, lineNo = 0;

            using var reader = new StreamReader(file.OpenReadStream());
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = line.Split('\t');
                if (cols.Length < 7)
                {
                    messages.Add($"Line {lineNo}: expected 7 tab-separated columns (Id, Name, Description, Price, Stock, IsAvailable, CategoryId), got {cols.Length}. Skipped.");
                    skipped++;
                    continue;
                }

                var id = cols[0].Trim();
                var name = cols[1].Trim();
                var description = cols[2].Trim();

                if (string.IsNullOrEmpty(id) || id.Length > 10)
                {
                    messages.Add($"Line {lineNo}: Id is missing or longer than 10 characters. Skipped.");
                    skipped++;
                    continue;
                }

                if (db.Products.Any(p => p.Id == id))
                {
                    messages.Add($"Line {lineNo} ({id}): a product with this Id already exists. Skipped.");
                    skipped++;
                    continue;
                }

                if (string.IsNullOrEmpty(name) || name.Length > 100)
                {
                    messages.Add($"Line {lineNo} ({id}): Name is missing or longer than 100 characters. Skipped.");
                    skipped++;
                    continue;
                }

                if (description.Length > 500)
                {
                    messages.Add($"Line {lineNo} ({id}): Description is longer than 500 characters. Skipped.");
                    skipped++;
                    continue;
                }

                if (!decimal.TryParse(cols[3].Trim(), out var price) || price <= 0)
                {
                    messages.Add($"Line {lineNo} ({id}): invalid Price '{cols[3].Trim()}'. Skipped.");
                    skipped++;
                    continue;
                }

                if (!int.TryParse(cols[4].Trim(), out var stock) || stock < 0)
                {
                    messages.Add($"Line {lineNo} ({id}): invalid Stock '{cols[4].Trim()}'. Skipped.");
                    skipped++;
                    continue;
                }

                bool isAvailable = cols[5].Trim() == "1";

                if (!int.TryParse(cols[6].Trim(), out var categoryId) || !db.Categories.Any(c => c.Id == categoryId))
                {
                    messages.Add($"Line {lineNo} ({id}): CategoryId '{cols[6].Trim()}' does not exist. Skipped.");
                    skipped++;
                    continue;
                }

                db.Products.Add(new Product
                {
                    Id = id,
                    Name = name,
                    Description = string.IsNullOrEmpty(description) ? null : description,
                    Price = price,
                    Stock = stock,
                    IsAvailable = isAvailable,
                    CategoryId = categoryId,
                });
                inserted++;
            }

            db.SaveChanges();

            TempData["Info"] = $"Batch insert done: {inserted} inserted, {skipped} skipped.";
            ViewBag.Messages = messages;
            return View();
        }        
        
        // GET: Product/Update
        [Authorize(Roles = "Admin")]
        public IActionResult Update(string? id)
        {
            var p = db.Products
                .Include(x => x.Photos)
                .FirstOrDefault(x => x.Id == id);

            if (p == null)
            {
                return RedirectToAction("Index");
            }

            var vm = new ProductUpdateViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                ExistingPhotos = p.Photos
                    .Select(ph => new ExistingPhotoViewModel { Id = ph.Id, PhotoUrl = ph.PhotoUrl })
                    .ToList(),
            };
            return View(vm);
        }

        // POST: Product/Update
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Update(ProductUpdateViewModel vm)
        {
            var p = db.Products
                .Include(x => x.Photos)
                .FirstOrDefault(x => x.Id == vm.Id);

            if (p == null)
            {
                return RedirectToAction("Manage");
            }

            if (vm.NewPhotos != null)
            {
                foreach (var file in vm.NewPhotos)
                {
                    var e = hp.ValidatePhoto(file);
                    if (e != "")
                    {
                        ModelState.AddModelError("NewPhotos", e);
                        break;
                    }
                }
            }

            // A product must keep at least one photo after removals + additions.
            int remainingCount = p.Photos.Count(ph => !vm.DeletePhotoIds.Contains(ph.Id))
                                  + (vm.NewPhotos?.Count ?? 0);
            if (remainingCount == 0)
            {
                ModelState.AddModelError("DeletePhotoIds", "A product must have at least one photo.");
            }

            if (ModelState.IsValid)
            {
                p.Name = vm.Name;
                p.Price = vm.Price;

                if (vm.DeletePhotoIds.Count > 0)
                {
                    var toDelete = p.Photos.Where(ph => vm.DeletePhotoIds.Contains(ph.Id)).ToList();
                    foreach (var photo in toDelete)
                    {
                        hp.DeletePhoto(photo.PhotoUrl, "products");
                        db.ProductPhotos.Remove(photo);
                    }
                }

                if (vm.NewPhotos != null)
                {
                    foreach (var file in vm.NewPhotos)
                    {
                        db.ProductPhotos.Add(new ProductPhoto
                        {
                            ProductId = p.Id,
                            PhotoUrl = hp.SavePhoto(file, "products"),
                        });
                    }
                }

                db.SaveChanges();

                TempData["Info"] = "Product updated.";
                return RedirectToAction("Manage");
            }

            vm.ExistingPhotos = p.Photos
                .Select(ph => new ExistingPhotoViewModel { Id = ph.Id, PhotoUrl = ph.PhotoUrl })
                .ToList();
            return View(vm);
        }

        // GET: Category/BatchUpdate
        [Authorize(Roles = "Admin")]
        public IActionResult BatchUpdate()
        {
            return View();
        }

        // POST: Category/BatchUpdate
        // Same tab-separated format as BatchInsert (Id, Name, DisplayOrder), but every
        // row updates an EXISTING category matched by Id — unknown Ids are skipped.
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> BatchUpdate(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please choose a text file.");
                return View();
            }

            var result = await BatchImportHelper.ProcessAsync(file, expectedColumns: 3, (cols, lineNo) =>
            {
                if (!int.TryParse(cols[0].Trim(), out var id))
                    return $"Line {lineNo}: invalid Id '{cols[0].Trim()}'. Skipped.";

                var category = db.Categories.Find(id);
                if (category == null)
                    return $"Line {lineNo}: no existing category with Id {id}. Skipped (use Batch Insert for new categories).";

                var name = cols[1].Trim();
                if (string.IsNullOrEmpty(name) || name.Length > 100)
                    return $"Line {lineNo} (Id {id}): Name is missing or longer than 100 characters. Skipped.";

                if (db.Categories.Any(c => c.Name == name && c.Id != id))
                    return $"Line {lineNo} (Id {id}): a category named '{name}' already exists. Skipped.";

                if (!int.TryParse(cols[2].Trim(), out var displayOrder))
                    return $"Line {lineNo} (Id {id}): invalid DisplayOrder '{cols[2].Trim()}'. Skipped.";

                category.Name = name;
                category.DisplayOrder = displayOrder;
                return null;
            });

            db.SaveChanges();

            TempData["Info"] = $"Batch update done: {result.Success} updated, {result.Skipped} skipped.";
            ViewBag.Messages = result.Messages;
            return View();
        }

        // POST: Product/Delete
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Delete(string? id)
        {
            var p = db.Products
                .Include(x => x.Photos)
                .FirstOrDefault(x => x.Id == id);

            if (p != null)
            {
                foreach (var photo in p.Photos)
                {
                    hp.DeletePhoto(photo.PhotoUrl, "products");
                }

                db.Products.Remove(p);
                db.SaveChanges();

                TempData["Info"] = "Product deleted.";
            }

            return RedirectToAction("Manage");
        }

        // POST: Category/BatchDelete
        // Deletes every checked category from the Index page — same "no products
        // under it" rule as the single Delete action, applied per row (rows that
        // still have products are skipped and reported instead of failing the batch).
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult BatchDelete(List<int>? ids)
        {
            if (ids == null || ids.Count == 0)
            {
                TempData["Info"] = "No categories selected.";
                return RedirectToAction("Index");
            }

            var categories = db.Categories.Where(c => ids.Contains(c.Id)).ToList();
            var messages = new List<string>();
            int deleted = 0;

            foreach (var c in categories)
            {
                if (db.Products.Any(p => p.CategoryId == c.Id))
                {
                    messages.Add($"'{c.Name}' (Id {c.Id}) still has products under it — skipped.");
                    continue;
                }

                db.Categories.Remove(c);
                deleted++;
            }

            db.SaveChanges();

            var summary = $"{deleted} categor{(deleted == 1 ? "y" : "ies")} deleted, {messages.Count} skipped.";
            TempData["Info"] = messages.Count > 0
                ? summary + "<br/>" + string.Join("<br/>", messages)
                : summary;

            return RedirectToAction("Index");
        }
    }
}