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

    [Range(0, 100)]
    public decimal? Grade { get; set; }

    public string? Feedback { get; set; }
}
