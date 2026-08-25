using Microsoft.AspNetCore.Mvc;
namespace Demo.Controllers
{
    public class OrderController : Controller
    {
        //GET: Order/Index
        public IActionResult Index()
        {
            return View();
        }
    }
}
