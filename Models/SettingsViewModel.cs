using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SchoolManagement.Models;

public class SettingsViewModel
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }

    public ChangePasswordViewModel ChangePassword { get; set; } = new();
    public SchoolBrandingViewModel Branding { get; set; } = new();
}

public class SchoolBrandingViewModel
{
    public string? CurrentLogoPath { get; set; }

    [Display(Name = "School Logo")]
    public IFormFile? LogoFile { get; set; }

    [Display(Name = "Theme Color")]
    public string ThemeColor { get; set; } = "#2563eb";
}

public class ChangePasswordViewModel
{
    [Required]
    [Display(Name = "Current Password")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [Display(Name = "New Password")]
    [DataType(DataType.Password)]
    [MinLength(6, ErrorMessage = "New password must be at least 6 characters.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Confirm New Password")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "New password and confirmation do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
