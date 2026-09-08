using System.ComponentModel.DataAnnotations;

namespace Demo.Models
{
    public class ModifierGroupInsertViewModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = "";

        [Required]
        public ModifierSelectionType SelectionType { get; set; } = ModifierSelectionType.Single;

        public bool IsRequired { get; set; }
    }

    public class ModifierGroupUpdateViewModel : ModifierGroupInsertViewModel
    {
        public int Id { get; set; }
    }
}