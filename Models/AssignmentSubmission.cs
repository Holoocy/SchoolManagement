using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class AssignmentSubmission : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    [Display(Name = "Status")]
    public string Status { get; set; } = "Assigned";

    [Display(Name = "Submitted On")]
    public DateTime? SubmittedOn { get; set; }

    // Score out of the assignment's total marks (MaxScore).
    public decimal? Grade { get; set; }

    public string? Feedback { get; set; }

    // Need Assignment loaded.
    public string? ScoreText => Grade == null ? null : $"{Grade.Value:0.##} / {Assignment?.MaxScore ?? 100}";
    public decimal? Percent => Grade == null || (Assignment?.MaxScore ?? 100) <= 0 ? null
        : Math.Round(Grade.Value * 100m / (Assignment?.MaxScore ?? 100), 1);
}
