using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace SchoolManagement.Models;

public static class QuestionTypes
{
    public const string MultipleChoice = "MultipleChoice";
    public const string TrueFalse = "TrueFalse";
    public const string FillInTheGap = "FillInTheGap";
    // An uploaded image/PDF question (tables, diagrams) answered in writing and marked by the teacher.
    public const string Uploaded = "Uploaded";

    public static readonly string[] All = { MultipleChoice, TrueFalse, FillInTheGap, Uploaded };

    public static string Label(string? type) => type switch
    {
        TrueFalse => "True or False",
        FillInTheGap => "Fill in the gap",
        Uploaded => "Uploaded question",
        _ => "Multiple choice"
    };
}

public class ExamQuestion : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int ExamId { get; set; }
    public Exam? Exam { get; set; }

    [Display(Name = "Question type")]
    public string QuestionType { get; set; } = QuestionTypes.MultipleChoice;

    // Optional when an attachment carries the whole question (e.g. a table or diagram).
    [Display(Name = "Question")]
    public string QuestionText { get; set; } = string.Empty;

    // Uploaded image or PDF for "Uploaded question" types (tables, diagrams).
    public string? AttachmentPath { get; set; }

    // Options A–D are only used by multiple-choice questions.
    [Display(Name = "Option A")]
    public string OptionA { get; set; } = string.Empty;

    [Display(Name = "Option B")]
    public string OptionB { get; set; } = string.Empty;

    [Display(Name = "Option C")]
    public string OptionC { get; set; } = string.Empty;

    [Display(Name = "Option D")]
    public string OptionD { get; set; } = string.Empty;

    // Multiple choice: "A"–"D". True/False: "True" or "False".
    // Fill in the gap: accepted answers, one per line.
    // Uploaded question: optional marking guide, shown only to the teacher.
    [Display(Name = "Correct Option")]
    public string CorrectOption { get; set; } = "A";

    // Written answers to uploaded questions are marked by the teacher, not automatically.
    public bool IsManuallyMarked => QuestionType == QuestionTypes.Uploaded;

    public bool HasImageAttachment =>
        AttachmentPath != null && !AttachmentPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<string> AcceptedAnswers =>
        CorrectOption.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsCorrect(string? answer)
    {
        if (IsManuallyMarked || string.IsNullOrWhiteSpace(answer)) return false;

        return QuestionType switch
        {
            QuestionTypes.FillInTheGap => AcceptedAnswers.Any(a => NormalizeGapAnswer(a) == NormalizeGapAnswer(answer)),
            _ => string.Equals(answer.Trim(), CorrectOption, StringComparison.OrdinalIgnoreCase)
        };
    }

    // Gap answers are marked leniently: case, surrounding/extra spaces and a trailing full stop are ignored.
    private static string NormalizeGapAnswer(string value) =>
        Regex.Replace(value.Trim().TrimEnd('.').Trim(), @"\s+", " ").ToLowerInvariant();
}
