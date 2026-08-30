namespace Demo.Models
{
    // Reuses CartItemViewModel (defined in CartViewModel.cs) since the checkout
    // summary needs the exact same shape: product name, price, quantity, stock, image.
    public class CheckoutViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal => Items.Sum(i => i.Price * i.Quantity);
        public decimal DeliveryFee { get; set; } = 0m;
        public decimal Total => Subtotal + DeliveryFee;
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

        // Kept both Status and PaymentStatus (same value) so this still matches
        // whatever the existing Confirmation.cshtml references — Order no longer
        // has a separate order-level status, only PaymentStatus (Unpaid/Paid).
        public string Status { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;

        public List<OrderConfirmationItemViewModel> Items { get; set; } = new();
        public decimal Total { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }
}