namespace Demo.Models
{
    public class CheckoutViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        // FIX: use LineTotal (which is UnitPrice * Quantity, i.e. Price + ModifiersExtraPrice)
        // so modifier prices are included, matching CartViewModel.Subtotal and the amount
        // actually charged in CheckoutController.PlaceOrder.
        public decimal Subtotal => Items.Sum(i => i.LineTotal);
        public decimal SST => Math.Round(Subtotal * 0.06m, 2);

        // Discount from the voucher selected on the client, echoed back only to
        // recompute this display total — the server always re-validates and
        // recalculates for real in CheckoutController.PlaceOrder.
        public decimal DiscountAmount { get; set; }
        public decimal Total => Subtotal + SST - DiscountAmount;

        public bool IsGuest { get; set; }

        // True when the logged-in user's Role is "Admin". Admins can place
        // orders but are not allowed to redeem vouchers.
        public bool IsAdmin { get; set; }

        // Members only — vouchers this specific user currently holds that are
        // neither used nor expired. Always empty for guests and admins.
        public List<VoucherOptionViewModel> AvailableVouchers { get; set; } = new();
    }

    public class VoucherOptionViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public decimal DiscountAmount { get; set; }
        public decimal MinimumSpend { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public class OrderConfirmationItemViewModel
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal => UnitPrice * Quantity;
    }

    public class OrderConfirmationViewModel
    {
        public int OrderId { get; set; }
        public DateTime? OrderDateTime { get; set; }

        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;

        public List<OrderConfirmationItemViewModel> Items { get; set; } = new();
        public decimal Subtotal { get; set; }
        public decimal SST { get; set; }
        public decimal DiscountAmount { get; set; }
        public string? VoucherCode { get; set; }
        public decimal Total { get; set; }
    }
}