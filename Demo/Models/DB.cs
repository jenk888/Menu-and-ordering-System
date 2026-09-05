using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Demo.Models;

public class DB(DbContextOptions options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<UserToken> UserTokens { get; set; }

    public DbSet<Category> Categories { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<ProductPhoto> ProductPhotos { get; set; }
    public DbSet<ModifierGroup> ModifierGroups { get; set; }
    public DbSet<ModifierOption> ModifierOptions { get; set; }

    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<CartItemModifier> CartItemModifiers { get; set; }

    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<OrderItemModifier> OrderItemModifiers { get; set; }

    public DbSet<VoucherRule> VoucherRules { get; set; }
    public DbSet<Voucher> Vouchers { get; set; }
}

// ============================================================================
// Enums
// ============================================================================
public enum ModifierSelectionType
{
    Single,   // radio button group (e.g. Size, Spice Level)
    Multi     // checkbox group (e.g. Toppings)
}

public enum PaymentMethod
{
    TouchNGo,
    CreditCard,
    Cash
}

public enum PaymentStatus
{
    Unpaid,
    Paid
}

public enum OrderItemStatus
{
    Queued,
    Served
}

public enum VoucherStatus
{
    Available,
    Used,
    Expired
}

// ============================================================================
// User (manual table — Role distinguishes "Member" vs "Admin"; Guests never get a row)
// ============================================================================
public class User
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required, MaxLength(10)]
    public string Role { get; set; } = null!; // "Member" or "Admin"

    [Required, MaxLength(255)]
    public string Password { get; set; } = null!; // hashed, never plain text

    [Required, MaxLength(100)]
    public string Email { get; set; } = null!;

    [MaxLength(255)]
    public string? ProfilePhoto { get; set; }

    public bool IsActive { get; set; } = true;

    public int FailedLoginCount { get; set; } = 0;

    public DateTime? LockoutUntil { get; set; }

    public List<Order> Orders { get; set; } = [];

    public List<CartItem> CartItems { get; set; } = [];

    public List<UserToken> UserTokens { get; set; } = [];
}

// Used for password-reset / email-verification links.
public class UserToken
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Token { get; set; } = null!;

    [Required, MaxLength(10)]
    public string TokenType { get; set; } = null!; // e.g. "RESET", "VERIFY"

    public bool IsUsed { get; set; }

    public DateTime? Expire { get; set; }

    [Required]
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}

// ============================================================================
// Catalog
// ============================================================================
[Index(nameof(Name), IsUnique = true)]
public class Category
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public List<Product> Products { get; set; } = [];
}

public class Product
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    // Quantity on hand. Independent from IsAvailable: Stock tracks inventory,
    // IsAvailable is the admin's manual show/hide switch.
    public int Stock { get; set; }

    // Out-of-stock / unavailable toggle: hidden/disabled on the storefront menu when false.
    public bool IsAvailable { get; set; } = true;

    [Required]
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public List<ProductPhoto> Photos { get; set; } = [];

    public List<ModifierGroup> ModifierGroups { get; set; } = [];
}

public class ProductPhoto
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string PhotoUrl { get; set; } = null!;

    [Required]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

// e.g. "Size" (Single, required), "Toppings" (Multi, optional), "Spice Level" (Single, required)
public class ModifierGroup
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    public ModifierSelectionType SelectionType { get; set; }

    public bool IsRequired { get; set; }

    [Required]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public List<ModifierOption> Options { get; set; } = [];
}

// e.g. "Large" (+RM2.00), "Extra Cheese" (+RM1.50), "Spicy" (+RM0.00)
public class ModifierOption
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Column(TypeName = "decimal(10,2)")]
    public decimal ExtraPrice { get; set; }

    [Required]
    public int ModifierGroupId { get; set; }
    public ModifierGroup ModifierGroup { get; set; } = null!;
}

// ============================================================================
// Cart (DB-backed, Members only — guests use a session cart, not these tables)
// ============================================================================
public class CartItem
{
    [Key]
    public int Id { get; set; }

    public int Quantity { get; set; }

    // Product price + selected modifiers, snapshot at add-to-cart time.
    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPriceSnapshot { get; set; }

    [Required]
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Required]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public List<CartItemModifier> SelectedModifiers { get; set; } = [];
}

public class CartItemModifier
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int CartItemId { get; set; }
    public CartItem CartItem { get; set; } = null!;

    [Required]
    public int ModifierOptionId { get; set; }
    public ModifierOption ModifierOption { get; set; } = null!;
}

// ============================================================================
// Voucher
// ============================================================================
// VoucherRule = the admin-defined rule: "spend at least RM {MinimumSpend}, get
// RM {DiscountAmount} off". This is a template, not something a user holds directly.
//
// Voucher = one instance issued to one specific user once the app determines they
// qualify for a rule. A user can be issued several Vouchers off the same VoucherRule
// (e.g. they qualify again on a later order) — each is tracked and used independently,
// so redeeming one never marks the others as used.
public class VoucherRule
{
    [Key]
    public int Id { get; set; }

    // Admin-facing label, e.g. "Spend RM50 Get RM5 Off". Not shown to the customer directly.
    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Column(TypeName = "decimal(10,2)")]
    public decimal MinimumSpend { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal DiscountAmount { get; set; }

    // Null = issued vouchers never expire. Otherwise: each voucher issued under this
    // rule is valid for this many days from the moment it's issued to a user.
    public int? ExpiryDurationDays { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Whether the app should keep issuing new vouchers under this rule.
    // Turning this off doesn't affect vouchers already issued to users.
    public bool IsActive { get; set; } = true;

    public List<Voucher> Vouchers { get; set; } = [];
}

// One voucher instance, belonging to exactly one user.
[Index(nameof(Code), IsUnique = true)]
public class Voucher
{
    [Key]
    public int Id { get; set; }

    // Unique per instance (not per rule) so a user's several vouchers from the
    // same rule can still be told apart and redeemed one at a time.
    [Required, MaxLength(30)]
    public string Code { get; set; } = null!;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    // Snapshot of VoucherRule.ExpiryDurationDays resolved at issue time, so a later
    // change to the rule's duration doesn't retroactively change vouchers already issued.
    public DateTime? ExpiresAt { get; set; }

    // Set only when this specific voucher is redeemed on an order. Null = not used yet.
    public DateTime? UsedAt { get; set; }

    [Required]
    public int VoucherRuleId { get; set; }
    public VoucherRule VoucherRule { get; set; } = null!;

    [Required]
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Derived, read-only — this is the actual state to check/display; UsedAt/ExpiresAt
    // are the source of truth so "used" and "expired" can never both apply or drift out of sync.
    [NotMapped]
    public VoucherStatus Status =>
        UsedAt.HasValue ? VoucherStatus.Used :
        (ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow) ? VoucherStatus.Expired :
        VoucherStatus.Available;
}

// ============================================================================
// Orders
// ============================================================================
public class Order
{
    [Key]
    public int Id { get; set; }

    // Only populated for guest orders.
    [MaxLength(100)]
    public string? GuestName { get; set; }

    [MaxLength(20)]
    public string? GuestPhone { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Total { get; set; }

    // Cash payments only: filled in by admin when marking the order Paid.
    [Column(TypeName = "decimal(10,2)")]
    public decimal? AmountTendered { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? ChangeGiven { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Null for guest orders.
    public int? UserId { get; set; }
    public User? User { get; set; }

    // Members only; always null for guest orders.
    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }

    public List<OrderItem> OrderItems { get; set; } = [];

    // Order only shows up in the kitchen Order Queue once PaymentStatus == Paid.
    [NotMapped]
    public bool IsInKitchenQueue => PaymentStatus == PaymentStatus.Paid;

    // Derived, read-only: true once every line item has been served.
    [NotMapped]
    public bool IsFullyServed => OrderItems.Count > 0 && OrderItems.All(i => i.Status == OrderItemStatus.Served);
}

public class OrderItem
{
    [Key]
    public int Id { get; set; }

    // Snapshots taken at order time so later Product edits don't rewrite past orders.
    [MaxLength(100)]
    public string ProductNameSnapshot { get; set; } = null!;

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPriceSnapshot { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal LineTotal { get; set; }

    // Per-product kitchen status — this is what the Order Queue toggles, not the order as a whole.
    public OrderItemStatus Status { get; set; } = OrderItemStatus.Queued;

    [Required]
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Required]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public List<OrderItemModifier> SelectedModifiers { get; set; } = [];
}

public class OrderItemModifier
{
    [Key]
    public int Id { get; set; }

    [MaxLength(100)]
    public string ModifierGroupNameSnapshot { get; set; } = null!;

    [MaxLength(100)]
    public string ModifierOptionNameSnapshot { get; set; } = null!;

    [Column(TypeName = "decimal(10,2)")]
    public decimal ExtraPriceSnapshot { get; set; }

    [Required]
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
}
