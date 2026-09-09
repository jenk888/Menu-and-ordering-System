using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers
{
    public class TableController : Controller
    {
        // GET: /Table/5
        // This route is what actually gets encoded into the table's QR code
        // (see OrderController.TableQrCode) — a plain URL, not a custom scheme,
        // so a phone's stock camera app can open it directly without any app.
        [HttpGet("Table/{number:int}")]
        public IActionResult Index(int number)
        {
            if (number < 1) return RedirectToAction("Index", "Product");

            // Session, not TempData — this needs to persist across the whole
            // browsing session (menu → cart → checkout), not just one redirect.
            HttpContext.Session.Set("TableNumber", number);
            TempData["Info"] = $"You're ordering for Table {number}.";

            return RedirectToAction("Index", "Product");
        }
    }
}
