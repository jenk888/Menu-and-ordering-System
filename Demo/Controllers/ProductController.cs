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
        public IActionResult GetProducts(int? categoryId = null)
        {
            var query = db.Products
                .Where(p => p.IsAvailable);

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            var products = query.Select(p => new
            {
                p.Id,
                p.Name,
                p.Price,
                p.Stock,
                // ProductPhoto.PhotoUrl only stores the bare filename (e.g. "hiteaset_1.png"),
                // matching what Helper.SavePhoto() returns — the actual files live in
                // wwwroot/photos/product/, so that folder needs to be prepended here.
                PhotoUrl = p.Photos.Select(ph => ph.PhotoUrl).FirstOrDefault() != null
                    ? "/photos/product/" + p.Photos.Select(ph => ph.PhotoUrl).FirstOrDefault()
                    : "/photos/no-image.jpg"
            }).ToList();

            return Json(products);
        }
    }
}