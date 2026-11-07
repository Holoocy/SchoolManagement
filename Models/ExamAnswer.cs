namespace SchoolManagement.Models;

// A student's answer to one exam question.
public class ExamAnswer : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int ExamSubmissionId { get; set; }
    public ExamSubmission? ExamSubmission { get; set; }

    public int ExamQuestionId { get; set; }
    public ExamQuestion? ExamQuestion { get; set; }

    public string AnswerText { get; set; } = string.Empty;

    // true/false once marked; null while a written answer is waiting for the teacher.
    public bool? IsCorrect { get; set; }
}
