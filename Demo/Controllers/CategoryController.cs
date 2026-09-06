using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class CategoryController(DB db) : Controller
    {
        
        // GET: Category/Index
        public IActionResult Index()
        {
            var model = db.Categories
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .ToList();
            
            return View(model);
        }


        // GET: Category/Insert
        public IActionResult Insert()
        {
            return View();
        }

        // POST: Category/Insert
        [HttpPost]
        public IActionResult Insert(CategoryInsertVM vm)
        {
            if (ModelState.IsValid("Name") && db.Categories.Any(c => c.Name == vm.Name))
            {
                ModelState.AddModelError("Name", "This category name already exists.");
            }

            if (ModelState.IsValid)
            {
                db.Categories.Add(new()
                {
                    Name = vm.Name,
                    DisplayOrder = vm.DisplayOrder,
                });
                db.SaveChanges();

                TempData["Info"] = "Category inserted.";
                return RedirectToAction("Index");
            }
            return View(vm);
        }

        // GET: Category/Update

        public IActionResult Update(int id)
        {
            var c = db.Categories.Find(id);

            if (c == null)
            {
                return RedirectToAction("Index");
            }

            var vm = new CategoryUpdateVM
            {
                Id = c.Id,
                Name = c.Name,
                DisplayOrder = c.DisplayOrder,
            };

            return View(vm);
        }

        // POST: Category/Update
        [HttpPost]
        public IActionResult Update(CategoryUpdateVM vm)
        {
            var c = db.Categories.Find(vm.Id);

            if (c == null)
            {
                return RedirectToAction("Index");
            }

            if (ModelState.IsValid("Name") && db.Categories.Any(x => x.Name == vm.Name && x.Id != vm.Id))
            {
                ModelState.AddModelError("Name", "This category name already exists.");
            }

            if (ModelState.IsValid)
            {
                c.Name = vm.Name;
                c.DisplayOrder = vm.DisplayOrder;
                db.SaveChanges();

                TempData["Info"] = "Category updated.";
                return RedirectToAction("Index");
            }

            return View(vm);
        }

        // POST: Category/Delete
        [HttpPost]
        public IActionResult Delete(int id)
        {
            var c = db.Categories.Find(id);

            if (c != null)
            {
                // Prevent deleting a category that still has products
                bool hasProducts = db.Products.Any(p => p.CategoryId == id);

                if (hasProducts)
                {
                    TempData["Info"] = "Cannot delete: this category still has products under it.";
                    return RedirectToAction("Index");
                }

                db.Categories.Remove(c);
                db.SaveChanges();

                TempData["Info"] = "Category deleted.";
            }

            return RedirectToAction("Index");
        }
    }
}
