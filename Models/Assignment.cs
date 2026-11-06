using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class Assignment : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    [Display(Name = "Subject")]
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

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

    [Display(Name = "Created On")]
    [DataType(DataType.Date)]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();
}
