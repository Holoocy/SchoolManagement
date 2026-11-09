namespace SchoolManagement.Models;

public class ExamSubmission : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int ExamId { get; set; }
    public Exam? Exam { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int Score { get; set; }
    public int TotalQuestions { get; set; }

    // Total marks available. Null for submissions made when every question was worth 1.
    public int? TotalMarks { get; set; }
    public int OutOf => TotalMarks ?? TotalQuestions;
    public decimal Percentage => OutOf > 0 ? Math.Round(Score * 100m / OutOf, 1) : 0m;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    // Stored per-question answers (submissions made before answers were recorded have none).
    public ICollection<ExamAnswer> Answers { get; set; } = new List<ExamAnswer>();

    // Requires Answers to be loaded.
    public bool AwaitingMarking => Answers.Any(a => a.IsCorrect == null);
}
