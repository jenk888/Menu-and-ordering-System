namespace Demo.Models
{
    public class VoucherClaimVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal MinimumSpend { get; set; }
        public decimal DiscountAmount { get; set; }
        public int? ExpiryDurationDays { get; set; }
        public bool AlreadyClaimed { get; set; }
    }
}
