using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SchoolManagement.Models;

public static class MessageAudience
{
    public const string Parent = "Parent";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
    public const string Admin = "Admin";
}

public class MessageComposeViewModel
{
    [Required]
    public string Audience { get; set; } = MessageAudience.Parent;

    [Required]
    public string Scope { get; set; } = MessageScope.Individual;

    [Display(Name = "Recipient")]
    public int? RecipientUserId { get; set; }

    [Display(Name = "Class")]
    public int? ClassId { get; set; }

    // Parent-only: which of the picked teacher's subjects this message concerns.
    // There is no free-text Subject field anymore - this becomes the stored subject
    // when provided, otherwise the server falls back to a generated one.
    public string? RegardingSubject { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public List<string> AvailableAudiences { get; set; } = new();
    public bool CanSendToSchool { get; set; }

    public List<SelectListItem> ParentOptions { get; set; } = new();
    public List<SelectListItem> TeacherOptions { get; set; } = new();
    public List<SelectListItem> ClassOptions { get; set; } = new();

    // TeacherUserId -> comma-joined subject names, used to populate the Parent's
    // "Regarding Subject" dropdown once a teacher recipient is picked.
    public Dictionary<int, string> TeacherSubjectNames { get; set; } = new();
}
