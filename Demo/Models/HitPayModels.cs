namespace HitPayIntegration.Models
{
    // Incoming request from your frontend checkout
    public class CreatePaymentRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty; // "card" or "ewallet"
        public string UserEmail { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    // Response returned back to frontend
    public class PaymentResponse
    {
        public bool Success { get; set; }
        public string RedirectUrl { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    // HitPay API json response structure
    public class HitPayCreatePaymentResponse
    {
        public string id { get; set; } = string.Empty;
        public string url { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
    }
}