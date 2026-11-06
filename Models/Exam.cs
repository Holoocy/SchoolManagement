using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public static class ExamStatus
{
    public const string Draft = "Draft";
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Published = "Published";
}

public class Exam : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int ClassId { get; set; }
    public Class? Class { get; set; }

    [Required]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = ExamStatus.Draft;

    public string? RejectionReason { get; set; }

    [Display(Name = "Start Date")]
    public DateTime? StartDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
    public ICollection<ExamSubmission> Submissions { get; set; } = new List<ExamSubmission>();
}
