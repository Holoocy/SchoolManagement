namespace SchoolManagement.Models;

// Scores recorded for one student in one subject for a term, typed on the score sheet
// or uploaded from a CSV/Excel file. A recorded component replaces the score worked out
// from the student's assignments, CA tests or exams in the app.
//
// Each score is stored with the "out of" it was entered against (the component weight
// at the time), so it scales correctly if the Admin later changes the weights.
public class SubjectScore : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public string Term { get; set; } = string.Empty;

    public decimal? AssignmentScore { get; set; }
    public decimal? AssignmentOutOf { get; set; }

    public decimal? CaScore { get; set; }
    public decimal? CaOutOf { get; set; }

    public decimal? ExamScore { get; set; }
    public decimal? ExamOutOf { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    public bool IsEmpty => AssignmentScore == null && CaScore == null && ExamScore == null;
}

// The three parts of a subject result.
public static class ScoreComponents
{
    public const string Assignment = "Assignment";
    public const string Ca = "CA";
    public const string Exam = "Exam";

    public static readonly string[] All = { Assignment, Ca, Exam };
}

// How a school combines scores, set by the Admin.
public class GradingScheme
{
    public int AssignmentWeight { get; set; } = 10;
    public int CaWeight { get; set; } = 20;
    public int ExamWeight { get; set; } = 70;
    public int Decimals { get; set; }
    public string CurrentTerm { get; set; } = School.DefaultTerm;

    public int Total => AssignmentWeight + CaWeight + ExamWeight;

    public int WeightOf(string component) => component switch
    {
        ScoreComponents.Assignment => AssignmentWeight,
        ScoreComponents.Ca => CaWeight,
        _ => ExamWeight
    };

    public decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    public string Format(decimal? value) => value == null ? "—" : value.Value.ToString(Decimals == 0 ? "0" : "0." + new string('#', Decimals));
}
