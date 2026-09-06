using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Demo.Models;

#nullable disable warnings
public class LoginVM
{
    [StringLength(100)]
    [EmailAddress]
    public string Email { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    public bool RememberMe { get; set; }
}

public class RegisterVM
{
    [Required(ErrorMessage = "Email is required.")]
    [StringLength(100)]
    [RegularExpression(@"^.+@gmail\.com$", ErrorMessage = "Only @gmail.com format is allowed.")]
    [Remote("CheckEmail", "User", ErrorMessage = "Duplicated {0}.")]
    public string Email { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    public string Name { get; set; }

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"01[0-9]-[0-9]{7,8}$", ErrorMessage = "Invalid format.")]
    [DisplayName("Phone Number")]
    public string Phone { get; set;  }

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [Required(ErrorMessage = "Confirm Password is required.")]
    [StringLength(100, MinimumLength = 5)]
    [Compare("Password", ErrorMessage = "Both passwords do not match.")]
    [DataType(DataType.Password)]
    [DisplayName("Confirm Password")]
    public string Confirm { get; set; }

    [Required(ErrorMessage = "Photo is required.")]
    public IFormFile Photo { get; set; }
}

public class UpdatePasswordVM
{
    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    [DisplayName("Current Password")]
    public string Current { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    [DisplayName("New Password")]
    public string New { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [Compare("New")]
    [DataType(DataType.Password)]
    [DisplayName("Confirm Password")]
    public string Confirm { get; set; }
}

public class UpdateProfileVM
{
    public string? Email { get; set; }

    [StringLength(100)]
    public string Name { get; set; }

    public string? PhotoURL { get; set; }

    public IFormFile? Photo { get; set; }
}

public class ResetPasswordVM
{
    [StringLength(100)]
    [EmailAddress]
    public string Email { get; set; }
}

public class EmailVM
{
    [StringLength(100)]
    [EmailAddress]
    public string Email { get; set; }

    public string Subject { get; set; }

    public string Body { get; set; }

    public bool IsBodyHtml { get; set; }
}