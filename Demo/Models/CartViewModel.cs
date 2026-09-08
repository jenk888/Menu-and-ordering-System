
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
        public string? GuestLineKey { get; set; } // guest lines only — see GuestCartLine.MakeKey
        public string ProductId { get; set; } = null!;
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int Stock { get; set; }
        public string? ImageUrl { get; set; }
        public List<CartItemModifierViewModel> SelectedModifiers { get; set; } = new();

        public decimal ModifiersExtraPrice => SelectedModifiers.Sum(m => m.ExtraPrice);
        public decimal UnitPrice => Price + ModifiersExtraPrice;
        public decimal LineTotal => UnitPrice * Quantity;
    }

    public class CartViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal => Items.Sum(i => i.LineTotal);
        public decimal Total => Subtotal;
    }

    // Passed via ViewBag to Product/Details when arriving from the cart's "edit" flow.
    // LineId is the CartItem.Id (as string) for members, or the composite guest key for guests.
    public class CartItemEditInfo
    {
        public string LineId { get; set; } = null!;
        public int Quantity { get; set; }
        public List<int> SelectedOptionIds { get; set; } = new();
    }

    // A single line in a guest's session-backed cart. Unlike a member's CartItem rows,
    // there's no database Id to key on, so lines are keyed by a composite string built
    // from the product + its exact modifier selection (MakeKey) — this lets a guest have
    // multiple lines for the same product with different modifiers, same as members can.
    public class GuestCartLine
    {
        public string ProductId { get; set; } = null!;
        public int Quantity { get; set; }
        public List<int> ModifierOptionIds { get; set; } = new();

        public static string MakeKey(string productId, IEnumerable<int> modifierOptionIds)
        {
            var sorted = modifierOptionIds.OrderBy(x => x).ToList();
            return sorted.Count == 0 ? productId : $"{productId}|{string.Join(",", sorted)}";
        }
    }
}