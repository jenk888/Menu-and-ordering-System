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

        //GET: User/Register
        public IActionResult Register()
        {
            return View();
        }
    }
}