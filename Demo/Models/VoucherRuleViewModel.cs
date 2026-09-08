using System.ComponentModel.DataAnnotations;


namespace Demo.Models
{
    public class VoucherRuleInsertViewModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = "";

        [Range(0, 10000)]
        public decimal MinimumSpend { get; set; }

        [Range(0.01, 10000)]
        public decimal DiscountAmount { get; set; }

        [Range(1, 365)]
        public int ExpiryDurationDays { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class VoucherRuleUpdateViewModel : VoucherRuleInsertViewModel
    {
        public int Id { get; set; }
    }
}
