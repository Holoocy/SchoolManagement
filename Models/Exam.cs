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

// Exams and CA (continuous assessment) tests are built and taken the same way;
// the kind decides which part of the student's result the score counts towards.
public static class ExamKinds
{
    public const string Exam = "Exam";
    public const string Ca = "CA";

    public static readonly string[] All = { Exam, Ca };

    public static string Label(string? kind) => kind == Ca ? "CA Test" : "Exam";
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

    [Display(Name = "Type")]
    public string Kind { get; set; } = ExamKinds.Exam;

    // Term the score counts towards (the school's current term when it was created).
    public string? Term { get; set; }

    public string KindLabel => ExamKinds.Label(Kind);
    public bool IsCaTest => Kind == ExamKinds.Ca;

    public string? RejectionReason { get; set; }

    // School-local date and time the exam opens (see SchoolClock).
    [Display(Name = "Start Date & Time")]
    public DateTime? StartDate { get; set; }

    [Display(Name = "Duration (minutes)")]
    [Range(5, 600, ErrorMessage = "Duration must be between 5 and 600 minutes.")]
    public int? DurationMinutes { get; set; }

    // When the automatic 24-hour reminder went out; cleared if the start time changes.
    public DateTime? ReminderSentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndDate => StartDate != null && DurationMinutes != null
        ? StartDate.Value.AddMinutes(DurationMinutes.Value)
        : null;

    public bool HasNotStarted(DateTime now) => StartDate != null && now < StartDate.Value;
    public bool HasEnded(DateTime now) => EndDate != null && now >= EndDate.Value;

    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
    public ICollection<ExamSubmission> Submissions { get; set; } = new List<ExamSubmission>();
}
