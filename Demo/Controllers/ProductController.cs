using Microsoft.AspNetCore.Mvc;
namespace Demo.Controllers
{
    public class ProductController(DB db) : Controller
    {
        //GET: Product/Index
        [Route("")]
        [Route("Product")]
        [Route("Product/Index")]
        public IActionResult Index()
        {
            ViewBag.Categories = db.Categories.ToList();
            return View();
        }

        // AJAX Endpoint for Filtering Products
        [HttpGet]
        public IActionResult GetProducts(string? categoryId = null)
        {
            var query = db.Products
                .Where(p => p.IsAvailable);

            if (!string.IsNullOrEmpty(categoryId) && categoryId != "ALL")
            {
                query = query.Where(p => p.CategoryId == categoryId);
            }

            var products = query.Select(p => new
            {
                p.Id,
                p.Name,
                p.UnitPrice,
                p.Stock,
                // Grabs the PhotoUrl of the first photo in the relation directly, or falls back to default if null
                PhotoUrl = p.Photos.Select(ph => ph.PhotoUrl).FirstOrDefault() ?? "/images/no-image.jpg"
            }).ToList();

            return Json(products);
        }
    }
}