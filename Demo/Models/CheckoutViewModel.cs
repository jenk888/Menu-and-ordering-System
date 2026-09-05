namespace Demo.Models
{
    public class CheckoutViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal => Items.Sum(i => i.Price * i.Quantity);
        public decimal SST => Math.Round(Subtotal * 0.06m, 2);
        public decimal Total => Subtotal + SST;

        public bool IsGuest { get; set; }
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
        public decimal Total { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }
}