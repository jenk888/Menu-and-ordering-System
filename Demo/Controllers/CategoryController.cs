using Microsoft.AspNetCore.Mvc;
namespace Demo.Controllers
{
    public class CategoryController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
