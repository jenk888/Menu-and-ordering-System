
using Microsoft.AspNetCore.Mvc;
namespace Demo.Controllers
{
    public class CartController : Controller
    {
        //GET: Cart/Index
        public IActionResult Index()
        {
            return View();
        }
    }
}
