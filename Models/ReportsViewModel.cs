namespace SchoolManagement.Models;

public class ReportsViewModel
{
    // Teachers see their own grades, subjects and assignments.
    public bool IsTeacher { get; set; }
    public bool HasTeacherProfile { get; set; } = true;

    public int TotalStudents { get; set; }
    public int TotalTeachers { get; set; }   // for teachers: number of their grades
    public int TotalSubjects { get; set; }

    public int TotalAssignments { get; set; }
    public List<StatusBreakdown> AssignmentStatusBreakdown { get; set; } = new();

    public int TotalWeeklyChecklists { get; set; }
    public List<StatusBreakdown> WeeklyChecklistStatusBreakdown { get; set; } = new();

    public decimal OverallAttendanceRate { get; set; }
    public List<AttendanceDaySummary> AttendanceLastSevenDays { get; set; } = new();

    public GradingScheme Scheme { get; set; } = new();
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public List<GradeResultSummary> Grades { get; set; } = new();
}

public class StatusBreakdown
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class AttendanceDaySummary
{
    public DateTime Date { get; set; }
    public int Present { get; set; }
    public int Total { get; set; }
    public decimal Rate => Total > 0 ? Math.Round(Present * 100m / Total, 1) : 0m;
}
