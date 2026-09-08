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
                db.Users.Add(new()
                {
                    Id = GenerateMemberId(),
                    Email = vm.Email,
                    Name = vm.Name,
                    Phone = vm.Phone,
                    Role = "Member",
                    Password = hp.HashPassword(vm.Password),
                    ProfilePhoto = hp.SavePhoto(vm.Photo, "photos"),
                    IsActive = true,
                    FailedLoginCount = 0,
                });
                db.SaveChanges();

                TempData["Info"] = "Register successfully. Please login";
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
            string email = "aaron@gmail.com";      // User.Identity!.Name!
            var user = db.Users.FirstOrDefault(u => u.Email == email);
            if (user == null) return NotFound();

            var vm = new UpdateProfileVM
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Phone = user.Phone,
                PhotoURL = user.ProfilePhoto
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
            string email = "aaron@gmail.com";      // User.Identity!.Name!
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
                user.Name = vm.Name;
                user.Email = vm.Email;
                user.Phone = vm.Phone;

                if (vm.Photo != null)
                {
                    user.ProfilePhoto = hp.SavePhoto(vm.Photo, "photos");
                }

                db.SaveChanges();
                TempData["Info"] = "Profile updated successfully.";
                return RedirectToAction("Profile");
            }

            //ViewBag.Id = user.Id;
            vm.PhotoURL = user.ProfilePhoto;
            return View(vm);
        }

        // POST: User/ChangePassword
        [HttpPost]
        //[Authorize]
        [ValidateAntiForgeryToken]
        public IActionResult ChangePassword(UpdatePasswordVM passwordVm)
        {
            string email = "aaron@gmail.com";      // User.Identity!.Name!
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
                PhotoURL = user.ProfilePhoto
            };

            // If failed
            ViewBag.ShowPasswordModal = true;
            TempData["Error"] = "Failed to update password. Please check your inputs.";
            
            return View("Profile", profileVm);
        }
    }
}