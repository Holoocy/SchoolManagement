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

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
