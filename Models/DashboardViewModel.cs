namespace SchoolManagement.Models;

public class DashboardViewModel
{
    public int TotalStudents { get; set; }
    public int TotalTeachers { get; set; }
    public int TotalAssignments { get; set; }
    public int PendingAssignments { get; set; }
    public int TotalWeeklyChecklists { get; set; }
    public int PendingWeeklyChecklists { get; set; }
    public int PresentToday { get; set; }
    public int TotalAttendanceRecordsToday { get; set; }
    public decimal AttendancePercentage { get; set; }
    public List<Assignment> RecentAssignments { get; set; } = new();
    public List<Student> RecentStudents { get; set; } = new();
    public List<WeeklyChecklistItem> UpcomingWeeklyChecklists { get; set; } = new();
    public decimal? AverageGrade { get; set; }
    public int SubmissionsToGrade { get; set; }
}

public class TeacherDashboardItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TeacherDashboardViewModel
{
    public string TeacherName { get; set; } = string.Empty;
    public bool HasTeacherProfile { get; set; }
    public List<TeacherDashboardItem> Subjects { get; set; } = new();
    public List<TeacherDashboardItem> Classes { get; set; } = new(); // Count = active students
    public int ActiveStudents { get; set; }
    public int InactiveStudents { get; set; }
    public int TotalAssignments { get; set; }
    public int PendingAssignments { get; set; }
    public List<Assignment> RecentAssignments { get; set; } = new();
    public int TotalWeeklyChecklists { get; set; }
    public int PendingWeeklyChecklists { get; set; }
    public int MyPendingWeeklyChecklists { get; set; }
    public int SubmissionsToGrade { get; set; }
    public decimal? AverageGradeGiven { get; set; }
    public int TotalExams { get; set; }
    public int ExamsNeedingAction { get; set; } // drafts and rejected exams
    public int ExamsAwaitingApproval { get; set; }
    public int ExamAnswersToMark { get; set; }
    public List<Exam> UpcomingExams { get; set; } = new();
    public int MarkedToday { get; set; }
    public int PresentToday { get; set; }
}

public class StudentDashboardViewModel
{
    public string StudentName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public List<string> Subjects { get; set; } = new();
    public int TotalAssignments { get; set; }
    public int PendingAssignments { get; set; }
    public List<AssignmentSubmission> RecentSubmissions { get; set; } = new();
    public int PresentCount { get; set; }
    public int TotalAttendanceRecords { get; set; }
    public decimal AttendanceRate { get; set; }
    public decimal? AverageGrade { get; set; }
}

public class ParentChildSummary
{
    public int StudentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public int PendingAssignments { get; set; }
    public int PresentCount { get; set; }
    public int TotalAttendanceRecords { get; set; }
    public decimal AttendanceRate { get; set; }
    public decimal? AverageGrade { get; set; }
}

public class ParentDashboardViewModel
{
    public List<ParentChildSummary> Children { get; set; } = new();
}
