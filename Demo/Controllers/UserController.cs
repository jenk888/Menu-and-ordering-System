using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
namespace Demo.Controllers

{
    public class UserController(DB db,
                               IWebHostEnvironment en,
                               Helper hp) : Controller
    {
        // GET: User/Login
        public IActionResult Login()
        {
            return View();
        }

        //GET: User/Logout
        public IActionResult Logout(string? returnURL)
        {
            TempData["Info"] = "Logout successfully.";

            hp.SignOut();

            return RedirectToAction("Index", "Product");
        }

        //GET: User/AccessDenied
        public IActionResult AccessDenied(string? returnURL)
        {
            return View();
        }

        // GET: User/CheckEmail
        [HttpGet]
        public IActionResult CheckEmail(string email, string id)
        {
            // Check if the email exists in the db, excluding the current user's own id
            bool emailExists = db.Users.Any(u => u.Email == email && u.Id != id);

            // If i exists for another user, return false 
            return Json(!emailExists);
        }

        //GET: User/Register
        public IActionResult Register()
        {
            return View();
        }

        //POST: User/Register
        [HttpPost]
        public IActionResult Register(RegisterVM vm)
        {
            if (ModelState.IsValid("Email") &&
                db.Users.Any(u => u.Email == vm.Email))
            {
                ModelState.AddModelError("Email", "Duplicated Email.");
            }

            if (ModelState.IsValid("Photo"))
            {
                var err = hp.ValidatePhoto(vm.Photo);
                if (err != "") ModelState.AddModelError("Photo", err);
            }

            if (ModelState.IsValid)
            {
                vm.Phone = vm.Phone?.Replace("-", "").Trim() ?? "";

                var newUser = new User
                {
                    Id = GenerateMemberId(),
                    Email = vm.Email,
                    Name = vm.Name,
                    Phone = vm.Phone,
                    Role = "Member",
                    Password = hp.HashPassword(vm.Password),
                    ProfilePhoto = hp.SavePhoto(vm.Photo, "photos/userprofile"),
                    IsActive = true,
                    FailedLoginCount = 0,
                };

                db.Users.Add(newUser);

                // give a voucher to new register member
                var welcomeRule = db.VoucherRules
                    .FirstOrDefault(r => r.Name == "New Member Welcome Voucher" && r.IsActive);

                string smsSimulationText = "";

                if (welcomeRule != null)
                {
                    // generate a random n unique voucher code
                    string voucherCode = "NEW" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

                    var voucher = new Voucher
                    {
                        UserId = newUser.Id,
                        VoucherRuleId = welcomeRule.Id,
                        Code = voucherCode, 
                        IssuedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.AddDays((double)(welcomeRule.ExpiryDurationDays ?? 30))
                    };
                    db.Vouchers.Add(voucher);

                    smsSimulationText = $" [SMS Sent to {newUser.Phone}: Welcome to Yellow Palace! Your welcome voucher code is {voucherCode}.]";
                }  

                // submit & save
                db.SaveChanges();

                TempData["Info"] = "Register successfully. Please login" + smsSimulationText;
                return RedirectToAction("Login");
            }

            return View();
        }

        public string GenerateMemberId()
        {
            string yearPrefix = DateTime.Now.ToString("yy") + "M";

            var lastUser = db.Users
                .Where(u => u.Id.StartsWith(yearPrefix))
                .OrderByDescending(u => u.Id)
                .FirstOrDefault();

            int nextNumber = 1;

            if (lastUser != null)
            {
                string lastNumberStr = lastUser.Id.Substring(yearPrefix.Length);

                if (int.TryParse(lastNumberStr, out int lastNum))
                {
                    nextNumber = lastNum + 1;
                }
            }

            return yearPrefix + nextNumber.ToString("D5");
        }

        // GET: User/Profile
        //[Authorize]
        public IActionResult Profile()
        {
            string email = "membertest2@gmail.com";      // User.Identity!.Name!
            var user = db.Users.FirstOrDefault(u => u.Email == email);
            if (user == null) return NotFound();

            var userVouchers = db.Vouchers
                .Where(v => v.UserId == user.Id)
                .Select(v => new ProfileVoucherVM
                {
                    Code = v.Code,
                    RuleName = v.VoucherRule.Name,
                    DiscountAmount = v.VoucherRule.DiscountAmount,
                    MinimumSpend = v.VoucherRule.MinimumSpend,
                    ExpiresAt = v.ExpiresAt ?? DateTime.Now,
                    Status = v.Status.ToString()
                })
                .ToList();

            var vm = new UpdateProfileVM
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Phone = user.Phone,
                PhotoURL = user.ProfilePhoto,
                Vouchers = GetUserVouchers(user.Id)
            };

            ViewBag.Id = user.Id;
            ViewBag.Phone = user.Phone;

            return View(vm);
        }

        // POST: User/Profile
        [HttpPost]
        //[Authorize]
        [ValidateAntiForgeryToken]
        public IActionResult Profile(UpdateProfileVM vm)
        {
            string email = "membertest2@gmail.com";      // User.Identity!.Name!
            var user = db.Users.FirstOrDefault(u => u.Email == email);
            if (user == null) return NotFound();

            // check if Email is used by others
            var currentUser = db.Users.FirstOrDefault(u => u.Id == vm.Id);
            if (currentUser != null && currentUser.Email != vm.Email)
            {
                if (db.Users.Any(u => u.Email == vm.Email))
                {
                    ModelState.AddModelError("Email", "Duplicated Email.");
                }
            }

            if (vm.Photo != null)
            {
                var err = hp.ValidatePhoto(vm.Photo);
                if (err != "") ModelState.AddModelError("Photo", err);
            }

            if (ModelState.IsValid)
            {
                vm.Phone = vm.Phone?.Replace("-", "").Trim() ?? "";

                user.Name = vm.Name;
                user.Email = vm.Email;
                user.Phone = vm.Phone;

                if (vm.Photo != null)
                {
                    // if user has existing profile pic (and its not empty), delete it from server's physical path
                    if (!string.IsNullOrEmpty(user.ProfilePhoto))
                    {
                        var oldImagePath = Path.Combine(en.WebRootPath, "photos/userprofile", user.ProfilePhoto);
                        if (System.IO.File.Exists(oldImagePath))
                        {
                            System.IO.File.Delete(oldImagePath);
                        }
                    }

                    // 2. save the new profile pic & update the db field
                    user.ProfilePhoto = hp.SavePhoto(vm.Photo, "photos/userprofile");
                }

                db.SaveChanges();
                TempData["Info"] = "Profile updated successfully.";
                return RedirectToAction("Profile");
            }

            //ViewBag.Id = user.Id;
            vm.PhotoURL = user.ProfilePhoto;
            vm.Vouchers = GetUserVouchers(user.Id);
            return View(vm);
        }

        // POST: User/ChangePassword
        [HttpPost]
        //[Authorize]
        [ValidateAntiForgeryToken]
        public IActionResult ChangePassword(UpdatePasswordVM passwordVm)
        {
            string email = "membertest2@gmail.com";      // User.Identity!.Name!
            var user = db.Users.FirstOrDefault(u => u.Email == email);
            if (user == null) return NotFound();

            // 1. Check if current password is correct
            bool isPasswordValid = false;
            try
            {
                isPasswordValid = hp.VerifyPassword(passwordVm.Current, user.Password);
            }
            catch
            {
                isPasswordValid = (passwordVm.Current == user.Password);
            }

            if (!isPasswordValid)
            {
                ModelState.AddModelError("Current", "Incorrect current password.");
            }

            // 2. Check new password same with confirm password
            if (passwordVm.New != passwordVm.Confirm)
            {
                ModelState.AddModelError("Confirm", "The new password and confirm password do not match.");
            }

            // 3. If verification succeeds
            if (ModelState.IsValid)
            {
                user.Password = hp.HashPassword(passwordVm.New);
                db.SaveChanges();
                TempData["Info"] = "Password updated successfully.";
                return RedirectToAction("Profile");
            }

            // 4. if not succeeds 
            var profileVm = new UpdateProfileVM
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Phone = user.Phone,
                PhotoURL = user.ProfilePhoto,
                Vouchers = GetUserVouchers(user.Id)
            };

            // If failed
            ViewBag.ShowPasswordModal = true;
            TempData["Error"] = "Failed to update password. Please check your inputs.";

            return View("Profile", profileVm);
        }

            private List<ProfileVoucherVM> GetUserVouchers(string userId)
        {
            return db.Vouchers
                .Where(v => v.UserId == userId)
                .Select(v => new ProfileVoucherVM
                {
                    Code = v.Code,
                    RuleName = v.VoucherRule.Name,
                    DiscountAmount = v.VoucherRule.DiscountAmount,
                    MinimumSpend = v.VoucherRule.MinimumSpend,
                    ExpiresAt = v.ExpiresAt ?? DateTime.Now,
                    Status = v.Status.ToString()
                })
                .ToList();
        }
    }
}