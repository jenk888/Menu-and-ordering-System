namespace Demo.Models
{
    public class CartItemModifierViewModel
    {
        public string Name { get; set; } = string.Empty;
        public decimal ExtraPrice { get; set; }
    }

    public class CartItemViewModel
    {
        public int CartItemId { get; set; }
        public string ProductId { get; set; } = null!;
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int Stock { get; set; }
        public string? ImageUrl { get; set; }
        public List<CartItemModifierViewModel> SelectedModifiers { get; set; } = new();

        // Sum of extra charges from selected modifiers, e.g. "Large" or "Extra Cheese".
        public decimal ModifiersExtraPrice => SelectedModifiers.Sum(m => m.ExtraPrice);

        // Base price + modifier extras, per unit.
        public decimal UnitPrice => Price + ModifiersExtraPrice;

        public decimal LineTotal => UnitPrice * Quantity;
    }

    public class CartViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal => Items.Sum(i => i.LineTotal);
        public decimal Total => Subtotal;
    }
}