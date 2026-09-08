using Demo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Demo.Controllers
{
    //[Authorize(Roles = "Admin")]
    public class ModifierGroupController(DB db) : Controller
    {
        // GET: ModifierGroup/Index
        public IActionResult Index()
        {
            var model = db.ModifierGroups
                .Include(g => g.Options)
                .Include(g => g.Products)
                .OrderBy(g => g.Name)
                .ToList();

            return View(model);
        }

        // GET: ModifierGroup/Insert
        public IActionResult Insert()
        {
            return View();
        }

        // POST: ModifierGroup/Insert
        [HttpPost]
        public IActionResult Insert(ModifierGroupInsertViewModel vm)
        {
            if (ModelState.IsValid("Name") && db.ModifierGroups.Any(g => g.Name == vm.Name))
            {
                ModelState.AddModelError("Name", "This modifier group name already exists.");
            }

            if (ModelState.IsValid)
            {
                var group = new ModifierGroup
                {
                    Name = vm.Name,
                    SelectionType = vm.SelectionType,
                    IsRequired = vm.IsRequired,
                };
                db.ModifierGroups.Add(group);
                db.SaveChanges();

                TempData["Info"] = "Modifier group created. You can now add options to it below.";
                return RedirectToAction("Update", new { id = group.Id });
            }

            return View(vm);
        }

        // GET: ModifierGroup/Update/{id}
        public IActionResult Update(int id)
        {
            var g = db.ModifierGroups
                .Include(x => x.Options)
                .FirstOrDefault(x => x.Id == id);

            if (g == null)
            {
                return RedirectToAction("Index");
            }

            var vm = new ModifierGroupUpdateViewModel
            {
                Id = g.Id,
                Name = g.Name,
                SelectionType = g.SelectionType,
                IsRequired = g.IsRequired,
            };

            ViewBag.Options = g.Options.OrderBy(o => o.Id).ToList();
            return View(vm);
        }

        // POST: ModifierGroup/Update
        [HttpPost]
        public IActionResult Update(ModifierGroupUpdateViewModel vm)
        {
            var g = db.ModifierGroups.Find(vm.Id);

            if (g == null)
            {
                return RedirectToAction("Index");
            }

            if (ModelState.IsValid("Name") && db.ModifierGroups.Any(x => x.Name == vm.Name && x.Id != vm.Id))
            {
                ModelState.AddModelError("Name", "This modifier group name already exists.");
            }

            if (ModelState.IsValid)
            {
                g.Name = vm.Name;
                g.SelectionType = vm.SelectionType;
                g.IsRequired = vm.IsRequired;
                db.SaveChanges();

                TempData["Info"] = "Modifier group updated.";
                return RedirectToAction("Update", new { id = vm.Id });
            }

            ViewBag.Options = db.ModifierOptions.Where(o => o.ModifierGroupId == vm.Id).OrderBy(o => o.Id).ToList();
            return View(vm);
        }

        // POST: ModifierGroup/AddOption
        [HttpPost]
        public IActionResult AddOption(int modifierGroupId, string name, decimal extraPrice)
        {
            var group = db.ModifierGroups.Find(modifierGroupId);
            if (group == null)
            {
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || extraPrice < 0)
            {
                TempData["Info"] = "Could not add option: please check the values entered.";
            }
            else
            {
                db.ModifierOptions.Add(new ModifierOption
                {
                    ModifierGroupId = modifierGroupId,
                    Name = name.Trim(),
                    ExtraPrice = extraPrice,
                });
                db.SaveChanges();

                TempData["Info"] = "Option added.";
            }

            return RedirectToAction("Update", new { id = modifierGroupId });
        }

        // POST: ModifierGroup/UpdateOption
        [HttpPost]
        public IActionResult UpdateOption(int id, string name, decimal extraPrice)
        {
            var option = db.ModifierOptions.Find(id);
            if (option == null)
            {
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || extraPrice < 0)
            {
                TempData["Info"] = "Could not update option: please check the values entered.";
            }
            else
            {
                option.Name = name.Trim();
                option.ExtraPrice = extraPrice;
                db.SaveChanges();

                TempData["Info"] = "Option updated.";
            }

            return RedirectToAction("Update", new { id = option.ModifierGroupId });
        }

        // POST: ModifierGroup/DeleteOption
        [HttpPost]
        public IActionResult DeleteOption(int id)
        {
            var option = db.ModifierOptions.Find(id);
            if (option != null)
            {
                var groupId = option.ModifierGroupId;
                db.ModifierOptions.Remove(option);
                db.SaveChanges();

                TempData["Info"] = "Option deleted.";
                return RedirectToAction("Update", new { id = groupId });
            }

            return RedirectToAction("Index");
        }

        // POST: ModifierGroup/Delete
        // Admin has already confirmed via the JS confirm() dialog in the view.
        // EF Core's default cascade-delete convention removes the group's Options
        // automatically (ModifierGroupId is a required FK), and clears its rows in
        // the auto-generated Products join table too — past OrderItemModifiers are
        // untouched since they only store snapshot values, not a live FK.
        [HttpPost]
        public IActionResult Delete(int id)
        {
            var g = db.ModifierGroups.Find(id);

            if (g != null)
            {
                db.ModifierGroups.Remove(g);
                db.SaveChanges();

                TempData["Info"] = "Modifier group deleted.";
            }

            return RedirectToAction("Index");
        }
    }
}