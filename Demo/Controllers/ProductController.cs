using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class ProductController(DB db, Helper hp) : Controller
    {
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

        // AJAX Endpoint for Filtering Products
        [HttpGet]
        public IActionResult GetProducts(int? categoryId = null)
        {
            var query = db.Products
                .Where(p => p.IsAvailable);

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
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
        public IActionResult Insert()
        {
            ViewBag.Categories = db.Categories.ToList();

            var vm = new ProductInsertVM
            {
                Price = 0.01m,
            };

            return View(vm);
        }

        // POST: Product/Insert
        [HttpPost]
        public IActionResult Insert(ProductInsertVM vm)
        {
            if (ModelState.IsValid("CategoryId") && !db.Categories.Any(c => c.Id == vm.CategoryId))
            {
                ModelState.AddModelError("CategoryId", "Invalid category.");
            }

            if (ModelState.IsValid("Photo"))
            {
                var e = hp.ValidatePhoto(vm.Photo);
                if (e != "") ModelState.AddModelError("Photo", e);
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

                p.Photos.Add(new ProductPhoto
                {
                    PhotoUrl = hp.SavePhoto(vm.Photo, "products"),
                });

                db.Products.Add(p);
                db.SaveChanges();

                TempData["Info"] = "Product inserted.";
                return RedirectToAction("Index");
            }

            ViewBag.Categories = db.Categories.ToList();
            return View(vm);
        }

        // GET: Product/Update
        public IActionResult Update(string? id)
        {
            var p = db.Products.Find(id);

            if (p == null)
            {
                return RedirectToAction("Index");
            }

            var vm = new ProductUpdateVM
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                PhotoURL = db.Entry(p).Collection(x => x.Photos).Query()
                             .Select(ph => ph.PhotoUrl).FirstOrDefault(),
            };
            return View(vm);
        }

        // POST: Product/Update
        [HttpPost]
        public IActionResult Update(ProductUpdateVM vm)
        {
            var p = db.Products.Find(vm.Id);

            if (p == null)
            {
                return RedirectToAction("Index");
            }

            if (vm.Photo != null)
            {
                var e = hp.ValidatePhoto(vm.Photo);
                if (e != "") ModelState.AddModelError("Photo", e);
            }

            if (ModelState.IsValid)
            {
                p.Name = vm.Name;
                p.Price = vm.Price;

                if (vm.Photo != null)
                {
                    var existing = db.Entry(p).Collection(x => x.Photos).Query().FirstOrDefault();

                    if (existing != null)
                    {
                        hp.DeletePhoto(existing.PhotoUrl, "products");
                        db.ProductPhotos.Remove(existing);
                    }

                    db.ProductPhotos.Add(new ProductPhoto
                    {
                        ProductId = p.Id,
                        PhotoUrl = hp.SavePhoto(vm.Photo, "products"),
                    });
                }
                db.SaveChanges();

                TempData["Info"] = "Product updated.";
                return RedirectToAction("Index");
            }

            vm.PhotoURL = db.Entry(p).Collection(x => x.Photos).Query()
                             .Select(ph => ph.PhotoUrl).FirstOrDefault();
            return View(vm);
        }

        // POST: Product/Delete
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

            return RedirectToAction("Index");
        }
    }
}