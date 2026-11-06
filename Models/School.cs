using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class School
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "School Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Slug { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Logo")]
    public string? LogoPath { get; set; }

    [Display(Name = "Theme Color")]
    public string ThemeColor { get; set; } = "#2563eb";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
