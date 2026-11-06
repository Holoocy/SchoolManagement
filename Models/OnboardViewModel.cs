using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SchoolManagement.Models;

public class OnboardViewModel
{
    [Required]
    [Display(Name = "School Name")]
    public string SchoolName { get; set; } = string.Empty;

    [Display(Name = "School Logo")]
    public IFormFile? LogoFile { get; set; }

    [Display(Name = "Theme Color")]
    public string ThemeColor { get; set; } = "#2563eb";

    [Required]
    [Display(Name = "Your Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Confirm Password")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Password and confirmation do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
