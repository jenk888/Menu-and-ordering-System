using System.ComponentModel.DataAnnotations;

namespace Demo.Models;

public class ProductInsertVM
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Please select a category.")]
    public int CategoryId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required, Range(0.01, 100000, ErrorMessage = "Price must be between 0.01 and 100000.")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "Please select a photo.")]
    public IFormFile Photo { get; set; } = null!;
}

public class ProductUpdateVM
{
    public string Id { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required, Range(0.01, 100000, ErrorMessage = "Price must be between 0.01 and 100000.")]
    public decimal Price { get; set; }

    public string? PhotoURL { get; set; }

    public IFormFile? Photo { get; set; }
}