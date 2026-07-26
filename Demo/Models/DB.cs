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
    public DbSet<Cart> Carts { get; set; }
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderDetail> OrderDetails { get; set; }
    public DbSet<Payment> Payments { get; set; }
}

public class User
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; }

    [Required, MaxLength(10)]
    public string Role { get; set; }

    [Required, MaxLength(255)]
    public string Password { get; set; }

    [Required, MaxLength(100)]
    public string Email { get; set; }

    [MaxLength(255)]
    public string ProfilePhoto { get; set; }

    public int FailedLoginCount { get; set; } = 0;

    public DateTime? LockoutUntil { get; set; }

    public List<Order> Orders { get; set; } = [];

    public Cart? Cart { get; set; }

    public List<UserToken> UserTokens { get; set; } = [];
}

public class UserToken
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [Required, MaxLength(255)]
    public string Token { get; set; }

    [Required, MaxLength(10)]
    public string TokenType { get; set; }

    public bool IsUsed { get; set; }
    public DateTime? Expire { get; set; }

    [Required]
    public string UserId { get; set; }

    public User User { get; set; }
}

public class Category
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; }

    public List<Product> Products { get; set; } = [];
}

public class Product
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal UnitPrice { get; set; }

    public bool IsAvailable { get; set; }

    public int Stock { get; set; }

    [Required]
    public string CategoryId { get; set; }
    public Category Category { get; set; }

    public List<ProductPhoto> Photos { get; set; } = [];
}

public class ProductPhoto
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [MaxLength(255)]
    public string PhotoUrl { get; set; }

    [Required]
    public string ProductId { get; set; }

    public Product Product { get; set; }

}

public class Cart
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [Required]
    public string UserId { get; set; }

    public User User { get; set; }

    public List<CartItem> CartItems { get; set; } = [];
}

public class CartItem
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [Required]
    public string CartId { get; set; }

    [Required]
    public string ProductId { get; set; }

    public int Quantity { get; set; }

    public Cart Cart { get; set; }

    public Product Product { get; set; }
}

public class Order
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    public DateTime? OrderDateTime { get; set; }

    [MaxLength(10)]
    public string Status { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal TotalAmount { get; set; }

    [Required]
    public string UserId { get; set; }

    public User User { get; set; }

    public List<OrderDetail> Details { get; set; } = [];

    public Payment? Payment { get; set; }
}

public class OrderDetail
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [Required]
    public string OrderId { get; set; }

    [Required]
    public string ProductId { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal UnitPrice { get; set; }

    public Order Order { get; set; }

    public Product Product { get; set; }
}

public class Payment
{
    [Key, MaxLength(5)]
    public string Id { get; set; }

    [MaxLength(100)]
    public string PaymentMethod { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal Amount { get; set; }

    [MaxLength(10)]
    public string Status { get; set; }

    public DateTime? PaidDate { get; set; }

    [Required]
    public string OrderId { get; set; }

    public Order Order { get; set; }
}

