using Demo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Demo.Controllers
{
    [Authorize(Roles = "Admin")]
    public class VoucherRulesController(DB db) : Controller
    {
        // GET: VoucherRules/Index
        public IActionResult Index()
        {
            var model = db.VoucherRules
                .OrderBy(vr => vr.Name)
                .ToList();

            return View(model);
        }

        // GET: VoucherRules/Insert
        public IActionResult Insert()
        {
            return View();
        }

        // POST: VoucherRules/Insert
        [HttpPost]
        public IActionResult Insert(VoucherRuleInsertViewModel vm)
        {
            if (ModelState.IsValid("Name") && db.VoucherRules.Any(vr => vr.Name == vm.Name))
            {
                ModelState.AddModelError("Name", "This voucher rule name already exists.");
            }

            if (vm.DiscountAmount <= 0)
            {
                ModelState.AddModelError("DiscountAmount", "Discount amount must be greater than 0.");
            }

            if (vm.MinimumSpend < 0)
            {
                ModelState.AddModelError("MinimumSpend", "Minimum spend cannot be negative.");
            }

            if (ModelState.IsValid)
            {
                db.VoucherRules.Add(new()
                {
                    Name = vm.Name,
                    MinimumSpend = vm.MinimumSpend,
                    DiscountAmount = vm.DiscountAmount,
                    ExpiryDurationDays = vm.ExpiryDurationDays,
                    IsActive = vm.IsActive
                });
                db.SaveChanges();

                TempData["Info"] = "Voucher rule inserted successfully.";
                return RedirectToAction("Index");
            }

            return View(vm);
        }

        // GET: VoucherRules/Update
        public IActionResult Update(int id)
        {
            var vr = db.VoucherRules.Find(id);

            if (vr == null)
            {
                return RedirectToAction("Index");
            }

            var vm = new VoucherRuleUpdateViewModel
            {
                Id = vr.Id,
                Name = vr.Name,
                MinimumSpend = vr.MinimumSpend,
                DiscountAmount = vr.DiscountAmount,
                ExpiryDurationDays = vr.ExpiryDurationDays ?? 0,
                IsActive = vr.IsActive
            };

            return View(vm);
        }

        // POST: VoucherRules/Update
        [HttpPost]
        public IActionResult Update(VoucherRuleUpdateViewModel vm)
        {
            var vr = db.VoucherRules.Find(vm.Id);

            if (vr == null)
            {
                return RedirectToAction("Index");
            }

            if (ModelState.IsValid("Name") && db.VoucherRules.Any(x => x.Name == vm.Name && x.Id != vm.Id))
            {
                ModelState.AddModelError("Name", "This voucher rule name already exists.");
            }

            if (vm.DiscountAmount <= 0)
            {
                ModelState.AddModelError("DiscountAmount", "Discount amount must be greater than 0.");
            }

            if (vm.MinimumSpend < 0)
            {
                ModelState.AddModelError("MinimumSpend", "Minimum spend cannot be negative.");
            }

            if (ModelState.IsValid)
            {
                vr.Name = vm.Name;
                vr.MinimumSpend = vm.MinimumSpend;
                vr.DiscountAmount = vm.DiscountAmount;
                vr.ExpiryDurationDays = vm.ExpiryDurationDays;
                vr.IsActive = vm.IsActive;

                db.SaveChanges();

                TempData["Info"] = "Voucher rule updated successfully.";
                return RedirectToAction("Index");
            }

            return View(vm);
        }

        // POST: VoucherRules/Delete
        [HttpPost]
        public IActionResult Delete(int id)
        {
            var vr = db.VoucherRules.Find(id);

            if (vr != null)
            {
                // Optional: Check if any users have already claimed a voucher based on this rule to prevent breaking foreign key relationships.
                bool hasVouchers = db.Vouchers.Any(v => v.VoucherRuleId == id);

                if (hasVouchers)
                {
                    TempData["Info"] = "Cannot delete: this voucher rule is already associated with existing user vouchers. Consider deactivating it instead.";
                    return RedirectToAction("Index");
                }

                db.VoucherRules.Remove(vr);
                db.SaveChanges();

                TempData["Info"] = "Voucher rule deleted.";
            }

            return RedirectToAction("Index");
        }
    }
}