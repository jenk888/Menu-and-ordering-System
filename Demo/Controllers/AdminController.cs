using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace Demo.Controllers
{
    //[Authorize(Roles = "Admin")]
    public class AdminController(DB db, Helper hp) : Controller
    {
        // GET: Admin/Index (Member Listing + Basic Searching + Sorting + Paging)
        public IActionResult Index(string search, string sortOrder, int page = 1)
        {
            int pageSize = 5;

            // 1. Fetch only users with "Member" role
            var members = db.Users.Where(u => u.Role == "Member").AsQueryable();

            // 2. Search logic (matches Name, Id; or Email)
            if (!string.IsNullOrEmpty(search))
            {
                members = members.Where(m => m.Name.Contains(search) ||
                                             m.Id.Contains(search) ||
                                             m.Email.Contains(search));
            }

            // 3. Sorting parameters for UI
            ViewBag.NameSortParm = string.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewBag.IdSortParm = sortOrder == "id" ? "id_desc" : "id";

            // Apply sorting
            members = sortOrder switch
            {
                "name_desc" => members.OrderByDescending(m => m.Name),
                "id" => members.OrderBy(m => m.Id),
                "id_desc" => members.OrderByDescending(m => m.Id),
                _ => members.OrderBy(m => m.Name),
            };

            // 4. Paging
            int totalMembers = members.Count();

            // Ensure page index is at least 1
            if (page < 1) page = 1;

            var pagedMembers = members.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Pass pagination and search states to View
            ViewBag.CurrentSearch = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalMembers / pageSize));
            ViewBag.CurrentSort = sortOrder;

            // 5. Return partial view if requested via AJAX
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_MemberListPartial", pagedMembers);
            }
            return View(pagedMembers);
        }

        // GET：Admin/AdminList (Admin Listing + Basic Searching + Sorting + Paging)
        public IActionResult AdminList(string search, string sortOrder, int page = 1)
        {
            int pageSize = 5;

            // 1. Fetch only users with "Admin" role
            var admins = db.Users.Where(u => u.Role == "Admin").AsQueryable();

            // 2. Search logic (matches Name, Id; or Email)
            if (!string.IsNullOrEmpty(search))
            {
                admins = admins.Where(a => a.Name.Contains(search) ||
                                             a.Id.Contains(search) ||
                                             a.Email.Contains(search));
            }

            // 3. Sorting parameters for UI
            ViewBag.NameSortParm = string.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewBag.IdSortParm = sortOrder == "id" ? "id_desc" : "id";

            // Apply sorting
            admins = sortOrder switch
            {
                "name_desc" => admins.OrderByDescending(a => a.Name),
                "id" => admins.OrderBy(a => a.Id),
                "id_desc" => admins.OrderByDescending(a => a.Id),
                _ => admins.OrderBy(a => a.Name),
            };

            // 4. Paging
            int totalAdmins = admins.Count();

            // Ensure page index is atleast 1
            if (page < 1) page = 1;

            var pagedAdmins = admins.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Pass pagination and search states to View
            ViewBag.CurrentSearch = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalAdmins / pageSize));
            ViewBag.CurrentSort = sortOrder;

            // 5. Return partial view if requested via AJAX
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_AdminListPartial", pagedAdmins);
            }
            return View(pagedAdmins);
        }
        // GET: Admin/AddAdmin
        public IActionResult AddAdmin()
        {
            return View();
        }

        // POST: Admin/AddAdmin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddAdmin(AdminRegisterVm vm)
        {
            // Check for duplicated email
            if (ModelState.IsValid("Email") && db.Users.Any(u => u.Email == vm.Email))
            {
                ModelState.AddModelError("Email", "Duplicated Email.");
            }
            
            // Validate photo using Helper
            if (ModelState.IsValid("Photo"))
            {
                var err = hp.ValidatePhoto(vm.Photo);
                if (err != "") ModelState.AddModelError("Photo", err);
            }

            // Validate password confirmation match
            if (vm.Password != vm.ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");
            }    

            if (ModelState.IsValid)
            {
                vm.Phone = vm.Phone?.Replace("-", "").Trim() ?? "";

                // Generate admin id (2xA00001)
                string yearPrefix = DateTime.Now.ToString("yy") + "A";
    
                var lastAdmin = db.Users
                    .Where(u => u.Role == "Admin" && u.Id.StartsWith(yearPrefix))
                    .OrderByDescending(u => u.Id)
                    .FirstOrDefault();

                int nextNumber = 1;

                if (lastAdmin != null)
                {
                    string lastNumberStr = lastAdmin.Id.Substring(yearPrefix.Length);
                    if (int.TryParse(lastNumberStr, out int lastNum))
                    {
                        nextNumber = lastNum + 1;
                    }
                }

                string newAdminId = yearPrefix + nextNumber.ToString("D5");

                // Create new admin entity with admin role
                db.Users.Add(new()
                {
                    Id = newAdminId,
                    Name = vm.Name,
                    Email = vm.Email,
                    Password = hp.HashPassword(vm.Password),
                    Phone = vm.Phone,
                    ProfilePhoto = hp.SavePhoto(vm.Photo, "photos/profile"),
                    Role = "Admin",
                    IsActive = true,
                    FailedLoginCount = 0
                });

                db.SaveChanges();

                TempData["Info"] = "New admin account created successfully.";
                return RedirectToAction("AdminList");
            }

            return View(vm);
        }

        // GET: /Admin/MemberDetails?id=26M0001
        [HttpGet("Admin/MemberDetails")]
        public IActionResult Details(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var user = db.Users.FirstOrDefault(u => u.Id == id);
            if (user == null) return NotFound();

            // Check the list of vouchers for member
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

            // Passed to the backend member details page via ViewBag
            ViewBag.UserVouchers = userVouchers;

            return View(user);
        }

        // POST: Admin/DeleteMember
        [HttpPost("Admin/DeleteMember")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteMember(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["Error"] = "Invalid member ID.";
                return RedirectToAction("Index");
            }

            var user = db.Users.FirstOrDefault(u => u.Id == id);
            if (user != null)
            {
                db.Users.Remove(user);
                db.SaveChanges();
                TempData["Info"] = $"Member {id} has been deleted successfully.";
            }
            else
            {
                TempData["Error"] = "Member not found.";
            }

            return RedirectToAction("Index");
        }

        // GET: Admin/AdminDetails
        [HttpGet("Admin/AdminDetails")]
        public IActionResult AdminDetails(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var admin = db.Users.FirstOrDefault(u => u.Id == id);
            if (admin == null) return NotFound();

            return View(admin);
        }

        // POST: Admin/DeleteAdmin
        [HttpPost("Admin/DeleteAdmin")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteAdmin(string id)
        {
            // get current admin id(login), to avoid deleting themselves
            var currentAdminId = HttpContext.Session.GetString("AdminId");
            if (id == currentAdminId)
            {
                TempData["Error"] = "You cannot delete your own active account!";
                return RedirectToAction("AdminList"); // go back to admin listing page
            }

            var admin = db.Users.FirstOrDefault(u => u.Id == id);
            if (admin != null)
            {
                db.Users.Remove(admin);
                db.SaveChanges();
                TempData["Info"] = $"Admin {id} has been deleted successfully.";
            }
            else
            {
                TempData["Error"] = "Admin not found.";
            }

            return RedirectToAction("AdminList");
        }
    }
}
