using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Demo.Helper;
using Demo.Models;

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
        public IActionResult Manage(string? search, int? categoryId)
        {
            var query = db.Products
                .Include(p => p.Category)
                .Include(p => p.Photos)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p => p.Name.Contains(search) || p.Id.Contains(search));
            }

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            var model = query
                .OrderBy(p => p.Id)
                .ToList();

            ViewBag.Search = search;
            ViewBag.CategoryId = categoryId;
            ViewBag.Categories = db.Categories.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToList();
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
        //[Authorize(Roles = "Admin")]
        public IActionResult Insert()
        {
            ViewBag.Categories = db.Categories.ToList();
            ViewBag.ModifierGroups = db.ModifierGroups.Include(g => g.Options).ToList();

            var vm = new ProductInsertViewModel
            {
                Price = 0.01m,
            };

            return View(vm);
        }

        // POST: Product/Insert
        //[Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Insert(ProductInsertViewModel vm)
        {
            if (ModelState.IsValid("CategoryId") && !db.Categories.Any(c => c.Id == vm.CategoryId))
            {
                ModelState.AddModelError("CategoryId", "Invalid category.");
            }

            // [Required] doesn't fire on an empty (non-null) list, so check explicitly.
            if (vm.Photos.Count == 0)
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

                if (vm.ModifierGroupIds.Count > 0)
                {
                    p.ModifierGroups = db.ModifierGroups
                        .Where(g => vm.ModifierGroupIds.Contains(g.Id))
                        .ToList();
                }

                db.Products.Add(p);
                db.SaveChanges();

                TempData["Info"] = "Product inserted.";
                return RedirectToAction("Manage");
            }

            ViewBag.Categories = db.Categories.ToList();
            ViewBag.ModifierGroups = db.ModifierGroups.Include(g => g.Options).ToList();
            return View(vm);
        }

        // GET: Product/BatchInsert
        //[Authorize(Roles = "Admin")]
        public IActionResult BatchInsert()
        {
            return View();
        }

        // POST: Product/BatchInsert
        //[Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> BatchInsert(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please choose a text file.");
                return View();
            }

            var result = await BatchImportHelper.ProcessAsync(file, expectedColumns: 7, (cols, lineNo) =>
            {
                var id = cols[0].Trim();
                var name = cols[1].Trim();
                var description = cols[2].Trim();

                if (string.IsNullOrEmpty(id) || id.Length > 10)
                    return $"Line {lineNo}: Id is missing or longer than 10 characters. Skipped.";

                if (db.Products.Any(p => p.Id == id))
                    return $"Line {lineNo} ({id}): a product with this Id already exists. Skipped.";

                if (string.IsNullOrEmpty(name) || name.Length > 100)
                    return $"Line {lineNo} ({id}): Name is missing or longer than 100 characters. Skipped.";

                if (description.Length > 500)
                    return $"Line {lineNo} ({id}): Description is longer than 500 characters. Skipped.";

                if (!decimal.TryParse(cols[3].Trim(), out var price) || price <= 0)
                    return $"Line {lineNo} ({id}): invalid Price '{cols[3].Trim()}'. Skipped.";

                if (!int.TryParse(cols[4].Trim(), out var stock) || stock < 0)
                    return $"Line {lineNo} ({id}): invalid Stock '{cols[4].Trim()}'. Skipped.";

                if (!int.TryParse(cols[6].Trim(), out var categoryId) || !db.Categories.Any(c => c.Id == categoryId))
                    return $"Line {lineNo} ({id}): CategoryId '{cols[6].Trim()}' does not exist. Skipped.";

                bool isAvailable = cols[5].Trim() == "1";

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
                return null;
            });

            db.SaveChanges();

            TempData["Info"] = $"Batch insert done: {result.Success} inserted, {result.Skipped} skipped.";
            ViewBag.Messages = result.Messages;
            return View();
        }

        // POST: Product/BatchUpdate
        //[Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> BatchUpdate(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please choose a text file.");
                return View();
            }

            var result = await BatchImportHelper.ProcessAsync(file, expectedColumns: 7, (cols, lineNo) =>
            {
                var id = cols[0].Trim();
                var product = db.Products.FirstOrDefault(p => p.Id == id);
                if (product == null)
                    return $"Line {lineNo}: no existing product with Id '{id}'. Skipped (use Batch Insert for new products).";

                var name = cols[1].Trim();
                var description = cols[2].Trim();

                if (string.IsNullOrEmpty(name) || name.Length > 100)
                    return $"Line {lineNo} ({id}): Name is missing or longer than 100 characters. Skipped.";

                if (description.Length > 500)
                    return $"Line {lineNo} ({id}): Description is longer than 500 characters. Skipped.";

                if (!decimal.TryParse(cols[3].Trim(), out var price) || price <= 0)
                    return $"Line {lineNo} ({id}): invalid Price '{cols[3].Trim()}'. Skipped.";

                if (!int.TryParse(cols[4].Trim(), out var stock) || stock < 0)
                    return $"Line {lineNo} ({id}): invalid Stock '{cols[4].Trim()}'. Skipped.";

                if (!int.TryParse(cols[6].Trim(), out var categoryId) || !db.Categories.Any(c => c.Id == categoryId))
                    return $"Line {lineNo} ({id}): CategoryId '{cols[6].Trim()}' does not exist. Skipped.";

                product.Name = name;
                product.Description = string.IsNullOrEmpty(description) ? null : description;
                product.Price = price;
                product.Stock = stock;
                product.IsAvailable = cols[5].Trim() == "1";
                product.CategoryId = categoryId;
                return null;
            });

            db.SaveChanges();

            TempData["Info"] = $"Batch update done: {result.Success} updated, {result.Skipped} skipped.";
            ViewBag.Messages = result.Messages;
            return View();
        }

        // GET: Product/Update
        //[Authorize(Roles = "Admin")]
        public IActionResult Update(string? id)
        {
            var p = db.Products
                .Include(x => x.Photos)
                .Include(x => x.ModifierGroups)
                .FirstOrDefault(x => x.Id == id);

            if (p == null)
            {
                return RedirectToAction("Index");
            }

            ViewBag.ModifierGroups = db.ModifierGroups.Include(g => g.Options).ToList();

            var vm = new ProductUpdateViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                ExistingPhotos = p.Photos
                    .Select(ph => new ExistingPhotoViewModel { Id = ph.Id, PhotoUrl = ph.PhotoUrl })
                    .ToList(),
                ModifierGroupIds = p.ModifierGroups.Select(g => g.Id).ToList(),
            };
            return View(vm);
        }

        // GET: Product/BatchUpdate
        //[Authorize(Roles = "Admin")]
        public IActionResult BatchUpdate()
        {
            return View();
        }

        // POST: Product/Update
        //[Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Update(ProductUpdateViewModel vm)
        {
            var p = db.Products
                .Include(x => x.Photos)
                .Include(x => x.ModifierGroups)
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

                p.ModifierGroups = db.ModifierGroups
                    .Where(g => vm.ModifierGroupIds.Contains(g.Id))
                    .ToList();

                db.SaveChanges();

                TempData["Info"] = "Product updated.";
                return RedirectToAction("Manage");
            }

            ViewBag.ModifierGroups = db.ModifierGroups.Include(g => g.Options).ToList();
            vm.ExistingPhotos = p.Photos
                .Select(ph => new ExistingPhotoViewModel { Id = ph.Id, PhotoUrl = ph.PhotoUrl })
                .ToList();
            return View(vm);
        }


        // POST: Product/Delete
        //[Authorize(Roles = "Admin")]
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

        // POST: Product/BatchDelete
        // Deletes every Product whose Id is checked on the Manage page (and their photos).
        //[Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult BatchDelete(List<string>? ids)
        {
            if (ids == null || ids.Count == 0)
            {
                TempData["Info"] = "No products selected.";
                return RedirectToAction("Manage");
            }

            var products = db.Products
                .Include(p => p.Photos)
                .Where(p => ids.Contains(p.Id))
                .ToList();

            foreach (var p in products)
            {
                foreach (var photo in p.Photos)
                {
                    hp.DeletePhoto(photo.PhotoUrl, "products");
                }
            }

            db.Products.RemoveRange(products);
            db.SaveChanges();

            TempData["Info"] = $"{products.Count} product(s) deleted.";
            return RedirectToAction("Manage");
        }
    }
}