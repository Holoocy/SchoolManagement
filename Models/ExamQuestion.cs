using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class ExamQuestion : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int ExamId { get; set; }
    public Exam? Exam { get; set; }

    [Required]
    [Display(Name = "Question")]
    public string QuestionText { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Option A")]
    public string OptionA { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Option B")]
    public string OptionB { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Option C")]
    public string OptionC { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Option D")]
    public string OptionD { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Correct Option")]
    public string CorrectOption { get; set; } = "A";
}
