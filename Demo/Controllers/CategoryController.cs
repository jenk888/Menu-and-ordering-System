using Demo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Demo.Helper;
using X.PagedList;
using X.PagedList.Extensions;

namespace Demo.Controllers
{
    public class CategoryController(DB db) : Controller
    {

        // GET: Category/Index
        public IActionResult Index(string? search, string? sort, string? dir,int page = 1)
        {
            if (page < 1)
            {
                return RedirectToAction("Index", new { search, sort, dir, page = 1 });
            }

            var query = db.Categories.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(c => c.Name.Contains(search));
            }

            // Apply sorting
            bool desc = dir == "desc";

            query = sort switch
            {
                "id" => desc ? query.OrderByDescending(c => c.Id) : query.OrderBy(c => c.Id),
                "name" => desc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
                "displayorder" => desc ? query.OrderByDescending(c => c.DisplayOrder) : query.OrderBy(c => c.DisplayOrder),
                _ => query.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name),
            };

            var model = query.ToPagedList(page, 10);

            if (page > model.PageCount && model.PageCount > 0)
            {
                return RedirectToAction("Index", new { search, sort, dir, page = model.PageCount });
            }

            ViewBag.Search = search;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            if (Request.IsAjax())
            {
                return PartialView("_ManageCategoriesTable", model);
            }

            return View(model);
        }

        // GET: Category/Insert
        //[Authorize(Roles = "Admin")]
        public IActionResult Insert()
        {
            return View();
        }

        // POST: Category/Insert
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Insert(CategoryInsertViewModel vm)
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
        [Authorize(Roles = "Admin")]
        public IActionResult Update(int id)
        {
            var c = db.Categories.Find(id);

            if (c == null)
            {
                return RedirectToAction("Index");
            }

            var vm = new CategoryUpdateViewModel
            {
                Id = c.Id,
                Name = c.Name,
                DisplayOrder = c.DisplayOrder,
            };

            return View(vm);
        }


        // GET: Category/BatchInsert
        [Authorize(Roles = "Admin")]
        public IActionResult BatchInsert()
        {
            return View();
        }

        // POST: Category/BatchInsert
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> BatchInsert(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please choose a text file.");
                return View();
            }

            var result = await BatchImportHelper.ProcessAsync(file, expectedColumns: 3, (cols, lineNo) =>
            {
                if (!int.TryParse(cols[0].Trim(), out var id) || id <= 0)
                    return $"Line {lineNo}: invalid Id '{cols[0].Trim()}'. Skipped.";

                if (db.Categories.Any(c => c.Id == id))
                    return $"Line {lineNo} (Id {id}): a category with this Id already exists. Skipped.";

                var name = cols[1].Trim();
                if (string.IsNullOrEmpty(name) || name.Length > 100)
                    return $"Line {lineNo} (Id {id}): Name is missing or longer than 100 characters. Skipped.";

                if (db.Categories.Any(c => c.Name == name))
                    return $"Line {lineNo} (Id {id}): a category named '{name}' already exists. Skipped.";

                if (!int.TryParse(cols[2].Trim(), out var displayOrder))
                    return $"Line {lineNo} (Id {id}): invalid DisplayOrder '{cols[2].Trim()}'. Skipped.";

                db.Categories.Add(new Category
                {
                    Id = id,
                    Name = name,
                    DisplayOrder = displayOrder,
                });
                return null;
            });

            // Category.Id is an identity column, so SaveChanges would normally ignore the Id we set above and let SQL Server assign its own. 
            // Wrapping the save in an explicit transaction with IDENTITY_INSERT toggled on lets our chosen Ids actually get used.
            if (result.Success > 0)
            {
                using var tx = db.Database.BeginTransaction();
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.Categories ON");
                db.SaveChanges();
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.Categories OFF");
                tx.Commit();
            }

            TempData["Info"] = $"Batch insert done: {result.Success} inserted, {result.Skipped} skipped.";
            ViewBag.Messages = result.Messages;
            return View();
        }


        // POST: Category/Update
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult Update(CategoryUpdateViewModel vm)
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

        // GET: Category/BatchUpdate
        [Authorize(Roles = "Admin")]
        public IActionResult BatchUpdate()
        {
            return View();
        }

        // POST: Category/BatchUpdate
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


        // POST: Category/Delete
        [Authorize(Roles = "Admin")]
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

        // POST: Category/BatchDelete
        // Deletes every checked category from the Index page — same "no products under it" rule as the single Delete action
        // applied per row (rows that still have products are skipped and reported instead of failing the batch).
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
