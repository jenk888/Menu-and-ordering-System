using System.ComponentModel.DataAnnotations;

namespace Demo.Models;

public class CategoryInsertViewModel
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Range(0, 999)]
    public int DisplayOrder { get; set; }
}

public class CategoryUpdateViewModel
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Range(0, 999)]
    public int DisplayOrder { get; set; }
}
