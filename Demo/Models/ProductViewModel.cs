using System.ComponentModel.DataAnnotations;


namespace Demo.Models;

public class ProductInsertViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Please select a category.")]
    public int CategoryId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required, Range(0.01, 100000, ErrorMessage = "Price must be between 0.01 and 100000.")]
    public decimal Price { get; set; }

    [Required, Range(0, int.MaxValue, ErrorMessage = "Stock must be a positive integer.")]
    public int Stock { get; set; }

    [MaxLength(500)]
    public string Description { get; set; } = null!;

    [Required(ErrorMessage = "Please select a photo.")]

    public List<IFormFile> Photos { get; set; } = [];

    // ModifierGroup.Id values the admin ticked to attach with this product
    public List<int> ModifierGroupIds { get; set; } = [];
}

public class ProductUpdateViewModel
{
    public string Id { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required, Range(0.01, 100000, ErrorMessage = "Price must be between 0.01 and 100000.")]
    public decimal Price { get; set; }
    [Required, Range(0, int.MaxValue, ErrorMessage = "Stock must be a positive integer.")]
    public int Stock { get; set; }

    [MaxLength(500)]
    public string Description { get; set; } = null!;

    // Photo already saved against this product, rendered with checkbox
    // tick ones to remove on save
    public List<ExistingPhotoViewModel> ExistingPhotos { get; set; } = [];

    // ProductPhoto.Id values the admin ticked for removal
    public List<int> DeletePhotoIds { get; set; } = [];

    // New selected photos to add to this product
    public List<IFormFile> NewPhotos { get; set; } = [];

    // ModifierGroup.Id values the admin ticked to attach with this product
    public List<int> ModifierGroupIds { get; set; } = [];
}

public class ExistingPhotoViewModel
{
    public int Id { get; set; }
    public string PhotoUrl { get; set; } = null!;
}