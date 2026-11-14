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

    // ---- Grading (set by the Admin) ----
    // What each part of a subject result is worth; together they make the total.
    public int AssignmentWeight { get; set; } = 10;
    public int CaWeight { get; set; } = 20;
    public int ExamWeight { get; set; } = 70;

    // Decimal places scores are rounded to (0 = whole numbers).
    public int ScoreDecimals { get; set; }

    // New assignments, CA tests and exams are recorded against this term.
    public string CurrentTerm { get; set; } = DefaultTerm;

    public const string DefaultTerm = "First Term 2026/2027";

    public GradingScheme ToGradingScheme() => new()
    {
        AssignmentWeight = AssignmentWeight,
        CaWeight = CaWeight,
        ExamWeight = ExamWeight,
        Decimals = ScoreDecimals,
        CurrentTerm = string.IsNullOrWhiteSpace(CurrentTerm) ? DefaultTerm : CurrentTerm
    };
}
