using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public static class ChecklistDocumentVisibility
{
    public const string Admin = "Admin";
    public const string TeacherOnly = "TeacherOnly";
}

public class WeeklyChecklistItem : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    [Required]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Assigned To")]
    public string? AssignedTo { get; set; }

    [Display(Name = "Due Date")]
    [DataType(DataType.Date)]
    public DateTime DueDate { get; set; } = DateTime.UtcNow.Date.AddDays(7);

    [Required]
    [Display(Name = "Status")]
    public string Status { get; set; } = "In Progress";

    [Display(Name = "Created On")]
    [DataType(DataType.Date)]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    [Display(Name = "Document")]
    public string? DocumentPath { get; set; }

    [Display(Name = "Document Visible To")]
    public string DocumentVisibility { get; set; } = ChecklistDocumentVisibility.TeacherOnly;

    public int? UploadedByUserId { get; set; }
}
