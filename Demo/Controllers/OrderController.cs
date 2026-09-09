using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using Demo.Hubs;
using QRCoder;
using X.PagedList.Extensions;

namespace Demo.Controllers
{
    public class OrderController(DB db, ReceiptService receiptService, IHubContext<OrderHub> hub) : Controller
    {
        // Same pattern used in CartController / CheckoutController: the login
        // cookie stores the user's Email in the Name claim, so we look the
        // User row up from that every request instead of storing the Id.
        private User? CurrentUser =>
            User.Identity?.IsAuthenticated == true
                ? db.Users.FirstOrDefault(u => u.Email == User.Identity!.Name)
                : null;

        // GET: Order/Index — no direct nav link points here yet, but it's a
        // sensible landing spot: send each role to the page they actually want.
        public IActionResult Index()
        {
            if (User.IsInRole("Admin")) return RedirectToAction("Manage");
            if (User.IsInRole("Member")) return RedirectToAction("History");
            return RedirectToAction("Index", "Product");
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

        // ====================================================================
        // MEMBER: Order History + Detail + Cancellation
        // ====================================================================

        // GET: Order/History
        [Authorize(Roles = "Member")]
        public IActionResult History(string? search, string? sort, string? dir, int page = 1)
        {
            var user = CurrentUser!;

            // (1) Searching ------------------------
            ViewBag.Search = search = search?.Trim() ?? "";

            var searched = db.Orders
                              .Include(o => o.OrderItems)
                              .Where(o => o.UserId == user.Id);

            if (search != "")
            {
                if (int.TryParse(search, out int id))
                {
                    searched = searched.Where(o => o.Id == id);
                }
                else
                {
                    searched = searched.Where(o => false);
                }
            }

            // (2) Sorting --------------------------
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            Func<Order, object> fn = sort switch
            {
                "Total" => o => o.Total,
                "Id" => o => o.Id,
                _ => o => o.CreatedAt,
            };

            var sorted = dir == "asc" ?
                         searched.OrderBy(fn) :
                         searched.OrderByDescending(fn);

            // (3) Paging ----------------------------
            if (page < 1)
            {
                return RedirectToAction(null, new { search, sort, dir, page = 1 });
            }

            var m = sorted.ToPagedList(page, 10);

            if (page > m.PageCount && m.PageCount > 0)
            {
                return RedirectToAction(null, new { search, sort, dir, page = m.PageCount });
            }

            if (Request.IsAjax()) return PartialView("_HistoryList", m);

            return View(m);
        }

        // GET: Order/Detail/{id}
        [Authorize(Roles = "Member")]
        public IActionResult Detail(int id)
        {
            var user = CurrentUser!;

            var m = db.Orders
                      .Include(o => o.OrderItems)
                          .ThenInclude(oi => oi.SelectedModifiers)
                      .Include(o => o.Voucher)
                      .FirstOrDefault(o => o.Id == id && o.UserId == user.Id);

            if (m == null) return RedirectToAction("History");

            return View(m);
        }

        // POST: Order/Cancel/{id}
        [Authorize(Roles = "Member")]
        [HttpPost]
        public async Task<IActionResult> Cancel(int id)
        {
            var user = CurrentUser!;

            var order = db.Orders
                          .Include(o => o.OrderItems)
                          .FirstOrDefault(o => o.Id == id && o.UserId == user.Id);

            if (order != null && order.CanBeCancelled)
            {
                order.IsCancelled = true;
                order.CancelledAt = DateTime.UtcNow;

                // Give the reserved stock back since the order never got paid.
                foreach (var item in order.OrderItems)
                {
                    var product = db.Products.Find(item.ProductId);
                    if (product != null) product.Stock += item.Quantity;
                }

                db.SaveChanges();
                TempData["Info"] = $"Order #{order.Id} has been cancelled.";

                await hub.Clients.All.SendAsync("OrderUpdated", order.Id);
            }

            return Redirect(Request.Headers.Referer.ToString());
        }

        // ====================================================================
        // ADMIN: Order Listing + Detail + Status Update
        // ====================================================================

        // GET: Order/Manage
        [Authorize(Roles = "Admin")]
        public IActionResult Manage(string? search, string? status, string? sort, string? dir, int page = 1)
        {
            // (1) Searching ------------------------
            ViewBag.Search = search = search?.Trim() ?? "";

            var searched = db.Orders
                              .Include(o => o.User)
                              .Include(o => o.OrderItems)
                              .AsQueryable();

            if (search != "")
            {
                if (int.TryParse(search, out int id))
                {
                    searched = searched.Where(o => o.Id == id);
                }
                else
                {
                    searched = searched.Where(o =>
                        (o.User != null && o.User.Name.Contains(search)) ||
                        (o.GuestName != null && o.GuestName.Contains(search)));
                }
            }

            // (1b) Status filter --------------------
            // OrderStatus is a computed [NotMapped] property, so it's filtered
            // here in terms of the real columns it's derived from.
            ViewBag.Status = status;

            searched = status switch
            {
                "Cancelled" => searched.Where(o => o.IsCancelled),
                "PendingPayment" => searched.Where(o => !o.IsCancelled && o.PaymentStatus == PaymentStatus.Unpaid),
                "Preparing" => searched.Where(o => !o.IsCancelled && o.PaymentStatus == PaymentStatus.Paid &&
                                                          o.OrderItems.Any(i => i.Status != OrderItemStatus.Served)),
                "Completed" => searched.Where(o => !o.IsCancelled && o.PaymentStatus == PaymentStatus.Paid &&
                                                          o.OrderItems.All(i => i.Status == OrderItemStatus.Served)),
                _ => searched,
            };

            // (2) Sorting --------------------------
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            Func<Order, object> fn = sort switch
            {
                "Id" => o => o.Id,
                "Total" => o => o.Total,
                _ => o => o.CreatedAt,
            };

            var sorted = dir == "asc" ?
                         searched.OrderBy(fn) :
                         searched.OrderByDescending(fn);

            // (3) Paging ----------------------------
            if (page < 1)
            {
                return RedirectToAction(null, new { search, status, sort, dir, page = 1 });
            }

            var m = sorted.ToPagedList(page, 10);

            if (page > m.PageCount && m.PageCount > 0)
            {
                return RedirectToAction(null, new { search, status, sort, dir, page = m.PageCount });
            }

            if (Request.IsAjax()) return PartialView("_ManageList", m);

            return View(m);
        }

        // GET: Order/ManageDetail/{id}
        [Authorize(Roles = "Admin")]
        public IActionResult ManageDetail(int id)
        {
            var m = db.Orders
                      .Include(o => o.User)
                      .Include(o => o.OrderItems)
                          .ThenInclude(oi => oi.SelectedModifiers)
                      .Include(o => o.Voucher)
                      .FirstOrDefault(o => o.Id == id);

            if (m == null) return RedirectToAction("Manage");

            return View(m);
        }

        // POST: Order/MarkPaid/{id}
        // Cash orders start Unpaid — this is how the admin settles them at the counter.
        // (Online payments are settled automatically by PaymentController's HitPay
        // webhook, not through here.)
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> MarkPaid(int id, decimal? amountTendered)
        {
            var order = db.Orders.Find(id);

            if (order != null && !order.IsCancelled && order.PaymentStatus == PaymentStatus.Unpaid)
            {
                order.PaymentStatus = PaymentStatus.Paid;

                if (order.PaymentMethod == PaymentMethod.Cash && amountTendered.HasValue)
                {
                    order.AmountTendered = amountTendered;
                    order.ChangeGiven = amountTendered.Value - order.Total;
                }

                db.SaveChanges();
                TempData["Info"] = $"Order #{order.Id} marked as paid.";

                await hub.Clients.All.SendAsync("OrderUpdated", order.Id);
            }

            return Redirect(Request.Headers.Referer.ToString());
        }

        // POST: Order/ToggleItemStatus/{orderItemId}
        // Per-product kitchen toggle: Queued <-> Served. The Order's own
        // OrderStatus (Preparing/Completed) is derived from these automatically.
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> ToggleItemStatus(int orderItemId)
        {
            var item = db.OrderItems.Find(orderItemId);

            if (item != null)
            {
                item.Status = item.Status == OrderItemStatus.Served
                    ? OrderItemStatus.Queued
                    : OrderItemStatus.Served;

                db.SaveChanges();

                await hub.Clients.All.SendAsync("OrderUpdated", item.OrderId);
            }

            return Redirect(Request.Headers.Referer.ToString());
        }

        // ====================================================================
        // ADMIN: Onscreen Reports (Chart.js, fed by two small JSON endpoints)
        // ====================================================================

        // GET: Order/Reports
        [Authorize(Roles = "Admin")]
        public IActionResult Reports()
        {
            return View();
        }

        // GET: Order/ReportsStatusData — pie chart source
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult ReportsStatusData()
        {
            var counts = db.Orders
                            .Include(o => o.OrderItems)
                            .AsEnumerable()
                            .GroupBy(o => o.OrderStatus)
                            .Select(g => new { status = g.Key.ToString(), count = g.Count() })
                            .ToList();

            return Json(counts);
        }

        // GET: Order/ReportsRevenueData?days=7 — bar chart source
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult ReportsRevenueData(int days = 7)
        {
            var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var raw = db.Orders
                        .Where(o => !o.IsCancelled &&
                                    o.PaymentStatus == PaymentStatus.Paid &&
                                    o.CreatedAt >= since)
                        .GroupBy(o => o.CreatedAt.Date)
                        .Select(g => new { date = g.Key, total = g.Sum(o => o.Total) })
                        .ToList();

            var series = Enumerable.Range(0, days)
                .Select(i => since.AddDays(i))
                .Select(d => new
                {
                    label = d.ToString("MM-dd"),
                    total = raw.FirstOrDefault(r => r.date == d)?.total ?? 0m
                });

            return Json(series);
        }

        // ====================================================================
        // TABLE QR: admin generates one QR per table; scanning it (with the
        // customer's own phone camera) opens TableController.Index, which
        // tags the browsing session so checkout knows which table to attach.
        // ====================================================================

        // GET: Order/Tables — admin picks a table to print/display its QR
        [Authorize(Roles = "Admin")]
        public IActionResult Tables(int count = 20)
        {
            ViewBag.Count = count;
            return View();
        }
    }
}
