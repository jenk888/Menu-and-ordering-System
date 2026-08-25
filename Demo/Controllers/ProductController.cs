using Microsoft.AspNetCore.Mvc;
namespace Demo.Controllers
{
    public class ProductController : Controller
    {
        //GET: Product/Index
        [Route("")]
        [Route("Product")]
        [Route("Product/Index")]
        public IActionResult Index()
        {
            return View();
        }
    }
}
