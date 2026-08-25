namespace Demo.Models
{
    public class CartItemViewModel
    {
        public string CartItemId { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int Stock { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class CartViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal => Items.Sum(i => i.Price * i.Quantity);
        public decimal DeliveryFee { get; set; } = 0m;
        public decimal Total => Subtotal + DeliveryFee;
    }
}
