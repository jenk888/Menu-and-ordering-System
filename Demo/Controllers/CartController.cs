
using Microsoft.AspNetCore.Mvc;
namespace Demo.Controllers
{
    public class CartController : Controller
    {
        public IActionResult Cart()
        {
            return View();
        }
    }
}
