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
    // A written answer counts as correct only when it gets full marks.
    public bool? IsCorrect { get; set; }

    // Marks the teacher gave a written answer (0 up to the question's Marks).
    public int? MarksAwarded { get; set; }

    // Marks this answer earns. Requires ExamQuestion to be loaded. Written answers
    // marked before scores existed were simply correct (full marks) or incorrect.
    public int Points => MarksAwarded ?? (IsCorrect == true ? ExamQuestion?.Marks ?? 1 : 0);
}
