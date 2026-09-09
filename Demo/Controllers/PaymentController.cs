using Demo.Models;
using Demo.Hubs;
using HitPayIntegration.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace HitPayIntegration.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DB _db;
        private readonly ReceiptService _receiptService;
        private readonly IMemoryCache _cache;
        private readonly IHubContext<OrderHub> _hub;

        public PaymentController(IConfiguration configuration, IHttpClientFactory httpClientFactory, DB db, ReceiptService receiptService, IMemoryCache cache, IHubContext<OrderHub> hub)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _db = db;
            _receiptService = receiptService;
            _cache = cache;
            _hub = hub;
        }

        // ----------------------------------------------------
        // 1. CREATE PAYMENT SESSION
        // ----------------------------------------------------
        // NOTE: No longer called by the main checkout flow - CheckoutController.PlaceOrder
        // now creates the order and initiates the HitPay payment together server-side, so
        // reference_number is always a real Order.Id. Left here in case you want a
        // standalone "retry payment" flow later, but if you build one, make sure it also
        // passes a real Order.Id as OrderId so the webhook below can find the order.
        [HttpPost("create-payment")]
        public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request)
        {
            var apiKey = _configuration["HitPay:ApiKey"];
            var baseUrl = _configuration["HitPay:BaseUrl"];
            var redirectUrl = _configuration["HitPay:RedirectUrl"];
            var webhookUrl = _configuration["HitPay:WebhookUrl"];

            // Map payment methods to HitPay method codes. Cash is handled entirely in
            // CheckoutController without calling HitPay at all (pay on pickup).
            // NOTE: "card" requires a connected Stripe account under Settings > Payment
            // Methods in the HitPay sandbox dashboard - if that's still erroring
            // ("trouble retrieving account details") on their end, card payments will
            // fail here too until that's resolved or HitPay support fixes it.
            var paymentMethods = request.PaymentMethod.ToLower() switch
            {
                "touchngo" => new List<string> { "touch_n_go" },
                _ => new List<string> { "card" }
            };

            // HitPay expects application/x-www-form-urlencoded format
            var formData = new Dictionary<string, string>
            {
                { "amount", request.Amount.ToString("0.00") },
                { "currency", "MYR" },
                { "email", request.UserEmail },
                { "name", request.UserName },
                { "reference_number", request.OrderId },
                { "redirect_url", redirectUrl ?? string.Empty },
                { "webhook", webhookUrl ?? string.Empty } // Must be publicly accessible
            };

            // Add payment_methods array values into form data
            int index = 0;
            foreach (var method in paymentMethods)
            {
                formData.Add($"payment_methods[{index}]", method);
                index++;
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-BUSINESS-API-KEY", apiKey);

            var content = new FormUrlEncodedContent(formData);
            var response = await client.PostAsync($"{baseUrl}/payment-requests", content);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                var hitPayResult = JsonConvert.DeserializeObject<HitPayCreatePaymentResponse>(responseString);

                return Ok(new PaymentResponse
                {
                    Success = true,
                    RedirectUrl = hitPayResult?.url ?? string.Empty
                });
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, new PaymentResponse
            {
                Success = false,
                ErrorMessage = $"HitPay API Error: {errorContent}"
            });
        }

        // ----------------------------------------------------
        // 2. WEBHOOK HANDLER (HMAC SHA-256 Verified)
        // ----------------------------------------------------
        [HttpPost("webhook")]
        {
            try
            {
                var salt = _configuration["HitPay:Salt"];
                var form = Request.Form;

                var dict = new Dictionary<string, string>();
                foreach (var key in form.Keys)
                {
                    dict[key] = form[key].ToString();
                }

                if (!dict.ContainsKey("hmac"))
                {
                    return BadRequest("HMAC signature missing.");
                }

                string receivedHmac = dict["hmac"];

                // Reconstruct signature string by sorting parameters alphabetically
                var sortedKeys = dict.Keys.Where(k => k != "hmac").OrderBy(k => k, StringComparer.Ordinal);
                var signatureBuilder = new StringBuilder();

                foreach (var key in sortedKeys)
                {
                    signatureBuilder.Append(key);
                    signatureBuilder.Append(dict[key]);
                }

                // Verify HMAC SHA-256
                string calculatedHmac = CalculateHmacSha256(signatureBuilder.ToString(), salt);

                if (!string.Equals(calculatedHmac, receivedHmac, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Invalid signature validation.");
                }

                // Payload is authentic! Process payment status.
                string referenceNumber = dict["reference_number"];
                string status = dict["status"]; // "completed", "failed", etc.

                // reference_number is the real Order.Id set in CheckoutController.PlaceOrder.
                if (!int.TryParse(referenceNumber, out var orderId))
                {
                    return BadRequest($"reference_number '{referenceNumber}' is not a valid order id.");
                }

                var order = _db.Orders.FirstOrDefault(o => o.Id == orderId);
                if (order == null)
                {
                    return NotFound($"No order found for reference_number '{referenceNumber}'.");
                }

                // PaymentStatus only has Unpaid/Paid (no Failed) - a failed/cancelled
                // payment just leaves the order Unpaid, same as before it was attempted.
                if (status.Equals("completed", StringComparison.OrdinalIgnoreCase) && order.PaymentStatus != PaymentStatus.Paid)
                {
                    order.PaymentStatus = PaymentStatus.Paid;
                    _db.SaveChanges();

                    // A member's Detail page or the admin Manage page may be open
                    // right now waiting on exactly this — push it immediately
                    // instead of making them refresh to see the payment land.
                    await _hub.Clients.All.SendAsync("OrderUpdated", order.Id);  // ✅ correct
                    var fullOrder = _receiptService.GetOrderForReceipt(order.Id)!;

                    // Cash orders never reach this webhook, so this path is implicitly non-cash only.
                    string? email;
                    if (fullOrder.UserId != null)
                    {
                        email = fullOrder.User?.Email;
                    }
                    else
                    {
                        _cache.TryGetValue($"guest-email-order-{order.Id}", out string? cachedEmail);
                        email = cachedEmail;
                    }

                    _receiptService.SendReceiptEmail(fullOrder, email);
                }

                return Ok("Webhook Handled Successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Webhook Exception: {ex.Message}");
            }
        }

        // ----------------------------------------------------
        // 3. USER REDIRECT CALLBACK
        // ----------------------------------------------------
        [HttpGet("callback")]
        public IActionResult PaymentCallback([FromQuery] string status, [FromQuery] string reference)
        {
            if (int.TryParse(reference, out var orderId))
            {
                return Redirect($"/Checkout/Confirmation/{orderId}");
            }

            var safeStatus = status ?? "Unknown";
            var safeReference = reference ?? "N/A";
            bool isCompleted = safeStatus.Equals("completed", StringComparison.OrdinalIgnoreCase);

            var html = $@"
            <!DOCTYPE html>
            <html lang=""en"">
            <head>
                <meta charset=""UTF-8"">
                <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                <title>Payment Status</title>
                <script src=""https://cdn.tailwindcss.com""></script>
            </head>
            <body class=""bg-slate-100 flex items-center justify-center min-h-screen p-4 font-sans"">
                <div class=""max-w-md w-full bg-white rounded-2xl shadow-xl border border-slate-200 p-8 text-center space-y-6"">
                    <div class=""mx-auto flex items-center justify-center h-16 w-16 rounded-full {(isCompleted ? "bg-emerald-100 ring-8 ring-emerald-50" : "bg-amber-100 ring-8 ring-amber-50")}"">
                        <svg class=""h-8 w-8 {(isCompleted ? "text-emerald-600" : "text-amber-600")}"" fill=""none"" stroke=""currentColor"" viewBox=""0 0 24 24"">
                            {(isCompleted
                                        ? @"<path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2.5"" d=""M5 13l4 4L19 7"" />"
                                        : @"<path stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""2"" d=""M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"" />")}
                        </svg>
                    </div>
                    <div>
                        <h2 class=""text-2xl font-bold text-slate-900"">Payment {(isCompleted ? "Processed" : "Notice")}</h2>
                        <p class=""text-sm text-slate-500 mt-1"">Status update received for your order.</p>
                    </div>
                    <div class=""bg-slate-50 rounded-xl p-4 text-left border border-slate-200 space-y-3"">
                        <div class=""flex justify-between items-center text-xs"">
                            <span class=""text-slate-500 font-medium"">Current Status</span>
                            <span class=""px-2.5 py-1 rounded-full {(isCompleted ? "bg-emerald-100 text-emerald-700" : "bg-amber-100 text-amber-700")} font-semibold uppercase tracking-wider text-[10px]"">
                                {safeStatus}
                            </span>
                        </div>
                        <div class=""flex justify-between items-center text-xs"">
                            <span class=""text-slate-500 font-medium"">Reference ID</span>
                            <span class=""font-mono text-slate-700 font-medium truncate max-w-[180px]"">{safeReference}</span>
                        </div>
                    </div>
                    <div class=""pt-2"">
                        <a href=""/"" class=""w-full inline-flex justify-center items-center py-3 px-4 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-700 transition duration-150 shadow-sm"">
                            Return to Store
                        </a>
                    </div>
                </div>
            </body>
            </html>";

            return Content(html, "text/html");
        }
        // Helper method for HMAC SHA-256 Signature calculation
        private static string CalculateHmacSha256(string data, string secret)
        {
            var encoding = new UTF8Encoding();
            byte[] keyByte = encoding.GetBytes(secret);
            byte[] messageBytes = encoding.GetBytes(data);

            using var hmacsha256 = new HMACSHA256(keyByte);
            byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
            return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
        }
    }
}