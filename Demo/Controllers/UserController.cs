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
        public bool CheckEmail(string email)
        {
            return !db.Users.Any(u => u.Email == email);
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

    }
}