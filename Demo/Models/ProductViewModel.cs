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

    [Required(ErrorMessage = "Please select a photo.")]
    public List<IFormFile> Photos { get; set; } = [];
}

public class ProductUpdateViewModel
{
    public string Id { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required, Range(0.01, 100000, ErrorMessage = "Price must be between 0.01 and 100000.")]
    public decimal Price { get; set; }

    // Photo already saved against this product, rendered with checkbox
    // tick ones to remove on save
    public List<ExistingPhotoViewModel> ExistingPhotos { get; set; } = [];

    // ProductPhoto.Id values the admin ticked for removal
    public List<int> DeletePhotoIds { get; set; } = [];

    // New selected photos to add to this product
    public List<IFormFile> NewPhotos { get; set; } = [];
}

public class ExistingPhotoViewModel
{
    public int Id { get; set; }
    public string PhotoUrl { get; set; } = null!;
}