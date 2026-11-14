using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class Assignment : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    [Display(Name = "Subject")]
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    // Every student in this grade receives the assignment.
    [Display(Name = "Grade")]
    public int? ClassId { get; set; }
    public Class? Class { get; set; }

    [Required]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Due Date")]
    [DataType(DataType.Date)]
    public DateTime DueDate { get; set; } = DateTime.UtcNow;

    [Display(Name = "Status")]
    public string Status { get; set; } = "In Progress";

    // Highest score a student can get; scores are recorded out of this.
    [Range(1, 1000, ErrorMessage = "Total marks must be between 1 and 1000.")]
    [Display(Name = "Total marks")]
    public int MaxScore { get; set; } = 100;

    // Term the assignment counts towards (the school's current term when it was created).
    public string? Term { get; set; }

    [Display(Name = "Created On")]
    [DataType(DataType.Date)]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();

    // Status shown on the Assignments page and dashboards, worked out from the students'
    // submissions. Requires Submissions to be loaded.
    public string ProgressStatus =>
        !Submissions.Any() ? "Not Assigned"
        : Submissions.All(s => s.Status == "Graded") ? "Graded"
        : Submissions.Any(s => s.Status == "Submitted" || s.Status == "Graded") ? "Submitted"
        : "Assigned";

    // Pending until every assigned student's work has been graded.
    public bool IsPending => ProgressStatus != "Graded";
}
