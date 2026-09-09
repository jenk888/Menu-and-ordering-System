using Demo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Demo.Controllers
{
    [Authorize]
    public class VoucherController(DB db) : Controller
    {
        // GET: Voucher/Index
        public IActionResult Index()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return RedirectToAction("Login", "User");

            // Rule Ids this member has already claimed
            var claimedRuleIds = db.Vouchers
                .Where(v => v.UserId == userId)
                .Select(v => v.VoucherRuleId)
                .ToList();

            var vm = db.VoucherRules
                .Where(r => r.IsActive)
                .OrderBy(r => r.Name)
                .Select(r => new VoucherClaimVM
                {
                    Id = r.Id,
                    Name = r.Name,
                    MinimumSpend = r.MinimumSpend,
                    DiscountAmount = r.DiscountAmount,
                    ExpiryDurationDays = r.ExpiryDurationDays,
                    AlreadyClaimed = claimedRuleIds.Contains(r.Id)
                })
                .ToList();

            return View(vm);
        }

        // POST: Voucher/Claim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Claim(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return RedirectToAction("Login", "User");

            var rule = db.VoucherRules.FirstOrDefault(r => r.Id == id && r.IsActive);
            if (rule == null)
            {
                TempData["Error"] = "This voucher is no longer available.";
                return RedirectToAction("Index");
            }

            bool alreadyClaimed = db.Vouchers.Any(v => v.UserId == userId && v.VoucherRuleId == id);
            if (alreadyClaimed)
            {
                TempData["Error"] = "You have already claimed this voucher.";
                return RedirectToAction("Index");
            }

            string code = "VC" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            db.Vouchers.Add(new Voucher
            {
                UserId = userId,
                VoucherRuleId = rule.Id,
                Code = code,
                IssuedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddDays(rule.ExpiryDurationDays ?? 30)
            });
            db.SaveChanges();

            TempData["Info"] = $"Voucher claimed! Your code: {code}";
            return RedirectToAction("Index");
        }
    }
}