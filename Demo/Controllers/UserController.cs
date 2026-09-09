using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Net.Mail;
using System.Security.Claims;
namespace Demo.Controllers

{
    public class UserController(DB db,
                               IWebHostEnvironment en,
                               Helper hp) : Controller
    {
        // How many failed attempts before the account gets locked.
        private const int MaxFailedAttempts = 3;

        // How long an account stays locked once triggered.
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        // ====================================================================
        // Login / Logout
        // ====================================================================

        // GET: User/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.CaptchaQuestion = hp.GenerateCaptcha();
            return View();
        }

        // POST: User/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Login(LoginVM vm, string? returnUrl)
        {
            if (!hp.VerifyCaptcha(vm.CaptchaAnswer))
            {
                ModelState.AddModelError("CaptchaAnswer", "Incorrect answer. Please try again.");
            }

            var user = db.Users.FirstOrDefault(u => u.Email == vm.Email);

            // Already locked out from earlier attempts?
            if (user != null && user.LockoutUntil.HasValue)
            {
                if (user.LockoutUntil.Value > DateTime.Now)
                {
                    return RedirectToAction("Lockout", new { until = user.LockoutUntil });
                }

                // Lockout period has passed on its own — clear it so login can proceed normally.
                user.FailedLoginCount = 0;
                user.LockoutUntil = null;
                db.SaveChanges();
            }

            bool passwordOk = user != null
                && !string.IsNullOrEmpty(vm.Password)
                && hp.VerifyPassword(vm.Password, user.Password);

            if (!passwordOk)
            {
                ModelState.AddModelError("", "Email or password is incorrect.");

                if (user != null)
                {
                    user.FailedLoginCount++;

                    if (user.FailedLoginCount >= MaxFailedAttempts)
                    {
                        user.LockoutUntil = DateTime.Now.Add(LockoutDuration);
                        db.SaveChanges();
                        return RedirectToAction("Lockout", new { until = user.LockoutUntil });
                    }

                    db.SaveChanges();
                }
            }
            else if (!user!.IsActive)
            {
                ModelState.AddModelError("", "Your account has not been activated yet. Please check your email for the activation link.");
            }

            if (ModelState.IsValid)
            {
                // Successful login — clear the failed-attempt counter.
                user!.FailedLoginCount = 0;
                user.LockoutUntil = null;
                db.SaveChanges();

                string? photoPath = null;
                if (!string.IsNullOrEmpty(user.ProfilePhoto))
                {
                    string folder = user.Role == "Admin" ? "photos/adminprofile" : "photos/userprofile";
                    photoPath = $"/{folder}/{user.ProfilePhoto}";
                }

                hp.SignIn(user.Id, user.Email, user.Role, vm.RememberMe, photoPath);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Product");
            }

            ViewBag.ReturnUrl = returnUrl;
            ViewBag.CaptchaQuestion = hp.GenerateCaptcha();
            return View(vm);
        }

        // GET/POST: User/Logout
        [HttpGet]
        [HttpPost]
        public IActionResult Logout(string? returnURL)
        {
            hp.SignOut();
            TempData["Info"] = "Logout successfully.";
            return RedirectToAction("Index", "Product");
        }

        // GET: User/Lockout
        // Shown after 3 consecutive failed login attempts for the same account.
        [HttpGet]
        public IActionResult Lockout(DateTime? until)
        {
            ViewBag.Until = until;
            return View();
        }

        // GET: User/AccessDenied
        // Shown when a logged-in user tries to visit a page their Role can't access.
        [HttpGet]
        public IActionResult AccessDenied(string? returnUrl)
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

        // ====================================================================
        // Email Verification (account activation)
        // ====================================================================
        // NOTE: Public self-registration issues accounts as active already (see
        // Register below), so nothing currently issues a "VERIFY" token. This
        // is kept so any account created with IsActive = false (e.g. a future
        // "Add Member" flow) can still be activated via an emailed link.

        // GET: User/ConfirmEmail?token=...
        [HttpGet]
        public IActionResult ConfirmEmail(string token)
        {
            var userToken = db.UserTokens
                .Include(t => t.User)
                .FirstOrDefault(t => t.Token == token && t.TokenType == "VERIFY");

            if (userToken == null || userToken.IsUsed || userToken.Expire < DateTime.Now)
            {
                ViewBag.Success = false;
                return View();
            }

            userToken.User.IsActive = true;
            userToken.IsUsed = true;

            // Give a welcome voucher now that the email is confirmed
            var welcomeRule = db.VoucherRules
                .FirstOrDefault(r => r.Name == "New Member Welcome Voucher" && r.IsActive);

            if (welcomeRule != null)
            {
                // generate a random unique voucher code
                string voucherCode = "NEW" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

                var voucher = new Voucher
                {
                    UserId = userToken.UserId,
                    VoucherRuleId = welcomeRule.Id,
                    Code = voucherCode,
                    IssuedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddDays((double)(welcomeRule.ExpiryDurationDays ?? 30))
                };
                db.Vouchers.Add(voucher);
                ViewBag.SmsSimulationText = $"[SMS Sent to {userToken.User.Phone}: Welcome to Yellow Palace! Your welcome voucher code is {voucherCode}.]";
            }

            db.SaveChanges();

            ViewBag.Success = true;
            return View();
        }

        // GET: User/ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // POST: User/ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPassword(ForgotPasswordVM vm)
        {
            if (ModelState.IsValid)
            {
                var user = db.Users.FirstOrDefault(u => u.Email == vm.Email);

                if (user != null)
                {
                    // Generate a one-time token for the reset link
                    string token = Guid.NewGuid().ToString("N");

                    db.UserTokens.Add(new UserToken
                    {
                        Token = token,
                        TokenType = "RESET",
                        UserId = user.Id,
                        IsUsed = false,
                        Expire = DateTime.Now.AddHours(1),
                    });
                    db.SaveChanges();

                    string resetLink = Url.Action("ResetPassword", "User", new { token }, Request.Scheme)!;

                    var mail = new MailMessage();
                    mail.To.Add(user.Email);
                    mail.Subject = "Password Reset Request";
                    mail.Body = $"Click the link below to reset your password:<br/><a href='{resetLink}'>{resetLink}</a><br/>This link expires in 1 hour.";
                    mail.IsBodyHtml = true;

                    // TODO: confirm this matches your teammate's actual Helper.SendEmail signature
                    hp.SendEmail(mail);
                }

                // Same message regardless of whether the email exists, so we don't leak which emails are registered.
                TempData["Info"] = "If that email is registered, a password reset link has been sent.";
                return RedirectToAction("Login");
            }

            return View(vm);
        }

        // GET: User/ResetPassword
        // User arrives here via the emailed link (?token=...)
        [HttpGet]
        public IActionResult ResetPassword(string token)
        {
            var userToken = db.UserTokens.FirstOrDefault(t => t.Token == token && t.TokenType == "RESET");

            if (userToken == null || userToken.IsUsed || userToken.Expire < DateTime.Now)
            {
                TempData["Error"] = "This password reset link is invalid or has expired. Please request a new one.";
                return RedirectToAction("ForgotPassword");
            }

            return View(new ResetPasswordVM { Token = token });
        }

        // POST: User/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetPassword(ResetPasswordVM vm)
        {
            var userToken = db.UserTokens
                .Include(t => t.User)
                .FirstOrDefault(t => t.Token == vm.Token && t.TokenType == "RESET");

            if (userToken == null || userToken.IsUsed || userToken.Expire < DateTime.Now)
            {
                ModelState.AddModelError("", "This password reset link is invalid or has expired. Please request a new one.");
            }

            if (ModelState.IsValid)
            {
                userToken!.User.Password = hp.HashPassword(vm.NewPassword);
                userToken.IsUsed = true;

                // A successful reset also lifts any active lockout.
                userToken.User.FailedLoginCount = 0;
                userToken.User.LockoutUntil = null;

                db.SaveChanges();

                TempData["Info"] = "Your password has been reset successfully. Please login with your new password.";
                return RedirectToAction("Login");
            }

            return View(vm);
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
                    IsActive = false,
                    FailedLoginCount = 0,
                };

                db.Users.Add(newUser);

                // submit & save
                db.SaveChanges();

                // Send an activation link so the user can confirm their email.
                // The welcome voucher is issued once they confirm (see ConfirmEmail), not here.
                string verifyToken = Guid.NewGuid().ToString("N");

                db.UserTokens.Add(new UserToken
                {
                    Token = verifyToken,
                    TokenType = "VERIFY",
                    UserId = newUser.Id,
                    IsUsed = false,
                    Expire = DateTime.Now.AddHours(24),
                });
                db.SaveChanges();

                string confirmLink = Url.Action("ConfirmEmail", "User", new { token = verifyToken }, Request.Scheme)!;

                var mail = new MailMessage();
                mail.To.Add(newUser.Email);
                mail.Subject = "Confirm your Yellow Palace account";
                mail.Body = $"Hi {newUser.Name},<br/>Please click the link below to activate your account:<br/><a href='{confirmLink}'>{confirmLink}</a><br/>This link expires in 24 hours.";
                mail.IsBodyHtml = true;

                try { hp.SendEmail(mail); } catch { }

                TempData["Info"] = "Registration successful! Please check your email to activate your account before logging in.";
                return RedirectToAction("Login");
            }

            return View(vm);
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
        [Authorize]
        public IActionResult Profile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
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
        [Authorize]
        [ValidateAntiForgeryToken]
        public IActionResult Profile(UpdateProfileVM vm)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
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
        [Authorize]
        [ValidateAntiForgeryToken]
        public IActionResult ChangePassword(UpdatePasswordVM passwordVm)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
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