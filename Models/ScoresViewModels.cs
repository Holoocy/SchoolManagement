using SchoolManagement.Services;

namespace SchoolManagement.Models;

public class ScoreSheetViewModel
{
    public GradingScheme Scheme { get; set; } = new();
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public List<Class> Classes { get; set; } = new();
    public List<Subject> Subjects { get; set; } = new();
    public int? ClassId { get; set; }
    public int? SubjectId { get; set; }
    public string? ClassName { get; set; }
    public string? SubjectName { get; set; }
    public bool IsAdmin { get; set; }
    public List<ScoreSheetRow> Rows { get; set; } = new();
    public bool HasErrors { get; set; }

    public bool Ready => ClassId != null && SubjectId != null;
}

public class ScoreSheetRow
{
    public int StudentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AdmissionNumber { get; set; } = string.Empty;
    public SubjectResult Result { get; set; } = new();
    public int? Position { get; set; }
    // Values the user typed, re-shown after a failed save; errors by component.
    public Dictionary<string, string?> Posted { get; set; } = new();
    public Dictionary<string, string> Errors { get; set; } = new();
}

public class ScoreSheetInput
{
    public int StudentId { get; set; }
    public string? Assignment { get; set; }
    public string? Ca { get; set; }
    public string? Exam { get; set; }

    public string? Get(string component) => component switch
    {
        ScoreComponents.Assignment => Assignment,
        ScoreComponents.Ca => Ca,
        _ => Exam
    };
}

public class ScoreUploadViewModel
{
    public GradingScheme Scheme { get; set; } = new();
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public List<Class> Classes { get; set; } = new();
    public List<Subject> Subjects { get; set; } = new();
    public int? ClassId { get; set; }
    public int? SubjectId { get; set; }
    public bool IsAdmin { get; set; }
    public ScoreImportResult? Result { get; set; }
}

public class GradingSetupViewModel
{
    public int AssignmentWeight { get; set; }
    public int CaWeight { get; set; }
    public int ExamWeight { get; set; }
    public int ScoreDecimals { get; set; }
    public string CurrentTerm { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
}

// ---------- student record (Students/Details, Reports/Student) ----------

public class StudentRecordViewModel
{
    public Student Student { get; set; } = null!;
    public GradingScheme Scheme { get; set; } = new();
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public List<StudentSubjectLine> Subjects { get; set; } = new();
    public List<AssignmentSubmission> Assignments { get; set; } = new();
    public List<ExamSubmission> CaTests { get; set; } = new();
    public List<ExamSubmission> Exams { get; set; } = new();
    public decimal? Average { get; set; }
    public int? Position { get; set; }
    public int ClassSize { get; set; }
    public int AttendanceDays { get; set; }
    public int PresentDays { get; set; }
    // Where the term picker posts back to.
    public string ReturnController { get; set; } = "Students";
    public string ReturnAction { get; set; } = "Details";
}

public class StudentSubjectLine
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public SubjectResult Result { get; set; } = new();
}

// ---------- reports ----------

public class ResultsReportViewModel
{
    public GradingScheme Scheme { get; set; } = new();
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public List<Class> Classes { get; set; } = new();
    public List<Subject> SubjectOptions { get; set; } = new();
    public int? ClassId { get; set; }
    public int? SubjectId { get; set; }
    public string? ClassName { get; set; }
    public string? SubjectName { get; set; }
    public bool IsAdmin { get; set; }
    public List<Subject> Columns { get; set; } = new();       // subjects shown (broadsheet)
    public List<ResultsReportRow> Rows { get; set; } = new();
    public Dictionary<int, decimal?> SubjectAverages { get; set; } = new();
}

public class ResultsReportRow
{
    public Student Student { get; set; } = null!;
    public Dictionary<int, SubjectResult> Results { get; set; } = new();
    public decimal? Average { get; set; }   // broadsheet: average of subject totals
    public decimal? Total { get; set; }     // single subject: the subject total
    public int? Position { get; set; }
}

public class GradeResultSummary
{
    public int ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public int Students { get; set; }
    public int StudentsWithScores { get; set; }
    public decimal? Average { get; set; }
    public string? TopStudent { get; set; }
}

// ---------- teacher profile (Admin) ----------

public class TeacherProfileViewModel
{
    public Teacher Teacher { get; set; } = null!;
    public User? Account { get; set; }
    public GradingScheme Scheme { get; set; } = new();
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public List<TeacherDashboardItem> Grades { get; set; } = new();     // Count = active students
    public List<TeacherSubjectLine> Subjects { get; set; } = new();
    public int ActiveStudents { get; set; }
    public List<TeacherAssignmentLine> Assignments { get; set; } = new();
    public List<TeacherExamLine> Exams { get; set; } = new();
    public List<TeacherResultLine> Results { get; set; } = new();
    public int SubmissionsToGrade { get; set; }
    public int ExamAnswersToMark { get; set; }
    public int ScoresRecordedThisTerm { get; set; }
    public int AttendanceRecords30Days { get; set; }
    public decimal? AttendanceRate30Days { get; set; }
    public int AttendanceDaysTaken30Days { get; set; }
    public int ChecklistPending { get; set; }
    public int ChecklistDone { get; set; }
    public int MessagesSent { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public class TeacherSubjectLine
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public List<string> Grades { get; set; } = new();
}

public class TeacherAssignmentLine
{
    public Assignment Assignment { get; set; } = null!;
    public int Assigned { get; set; }
    public int Graded { get; set; }
    public decimal? AveragePercent { get; set; }
}

public class TeacherExamLine
{
    public Exam Exam { get; set; } = null!;
    public int Questions { get; set; }
    public int Submissions { get; set; }
    public int AwaitingMarking { get; set; }
    public decimal? AveragePercent { get; set; }
}

public class TeacherResultLine
{
    public int ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int Students { get; set; }
    public int WithScores { get; set; }
    public decimal? Average { get; set; }
}
