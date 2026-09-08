using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Demo.Controllers
{
    public class OrderController(DB db, ReceiptService receiptService) : Controller
    {
        //GET: Order/Index
        public IActionResult Index()
        {
            return View();
        }

        // GET: Order/Receipt/{id}
        // Shared by BOTH the admin order page and the member order history page —
        // link a button to this same URL from each, no duplicate PDF code anywhere.
        [Authorize]
        public IActionResult Receipt(int id)
        {
            var order = receiptService.GetOrderForReceipt(id);
            if (order == null) return NotFound();

            var isAdmin = User.IsInRole("Admin");
            var isOwner = order.UserId != null && order.User?.Email == User.Identity!.Name;

            if (!isAdmin && !isOwner) return Forbid();

            var pdfBytes = receiptService.GenerateReceiptPdf(order);
            return File(pdfBytes, "application/pdf", $"Receipt_Order{order.Id}.pdf");
        }
    }
}
