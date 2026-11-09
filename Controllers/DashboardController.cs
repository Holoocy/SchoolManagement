using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DashboardController> _logger;
    private readonly ISchoolClock _clock;

    public DashboardController(ApplicationDbContext context, ILogger<DashboardController> logger, ISchoolClock clock)
    {
        _context = context;
        _logger = logger;
        _clock = clock;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Admin"))
        {
            return View("Index", await BuildAdminDashboardAsync());
        }

        if (User.IsInRole("Teacher"))
        {
            return View("TeacherIndex", await BuildTeacherDashboardAsync());
        }

        if (User.IsInRole("Student"))
        {
            return View("StudentIndex", await BuildStudentDashboardAsync());
        }

        if (User.IsInRole("Parent"))
        {
            return View("ParentIndex", await BuildParentDashboardAsync());
        }

        return View("Index", new DashboardViewModel());
    }

    private async Task<DashboardViewModel> BuildAdminDashboardAsync()
    {
        var today = DateTime.UtcNow.Date;

        var model = new DashboardViewModel
        {
            TotalStudents = await _context.Students.CountAsync(),
            TotalTeachers = await _context.Teachers.CountAsync(),
            TotalAssignments = await _context.Assignments.CountAsync(),
            // Same rule as the Assignments page: pending until every submission is graded.
            PendingAssignments = await _context.Assignments
                .CountAsync(a => !a.Submissions.Any() || a.Submissions.Any(s => s.Status != "Graded")),
            TotalWeeklyChecklists = await _context.WeeklyChecklistItems.CountAsync(),
            PendingWeeklyChecklists = await _context.WeeklyChecklistItems.CountAsync(w => w.Status != "Done"),
            RecentAssignments = await _context.Assignments
                .Include(a => a.Submissions)
                .OrderByDescending(a => a.DueDate)
                .Take(5)
                .ToListAsync(),
            RecentStudents = await _context.Students
                .OrderByDescending(s => s.EnrollmentDate)
                .Take(5)
                .ToListAsync(),
            UpcomingWeeklyChecklists = await _context.WeeklyChecklistItems
                .OrderBy(w => w.DueDate)
                .Take(5)
                .ToListAsync()
        };

        var attendanceRecordsToday = await _context.AttendanceRecords
            .Where(a => a.AttendanceDate.Date == today)
            .ToListAsync();

        model.TotalAttendanceRecordsToday = attendanceRecordsToday.Count;
        model.PresentToday = attendanceRecordsToday.Count(a => a.Status == "Present");
        model.AttendancePercentage = attendanceRecordsToday.Count > 0
            ? Math.Round((decimal)model.PresentToday * 100m / attendanceRecordsToday.Count, 1)
            : 0m;

        var grades = await _context.AssignmentSubmissions
            .Where(s => s.Grade != null)
            .Select(s => s.Grade!.Value)
            .ToListAsync();
        model.AverageGrade = grades.Any() ? Math.Round(grades.Average(), 1) : (decimal?)null;
        model.SubmissionsToGrade = await _context.AssignmentSubmissions.CountAsync(s => s.Status == "Submitted");

        return model;
    }

    private async Task<TeacherDashboardViewModel> BuildTeacherDashboardAsync()
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        var model = new TeacherDashboardViewModel
        {
            TeacherName = User.FindFirstValue("FullName") ?? "Teacher"
        };

        if (teacher == null)
        {
            return model;
        }

        model.HasTeacherProfile = true;

        var subjectIds = await _context.SubjectTeachers
            .Where(st => st.TeacherId == teacher.Id)
            .Select(st => st.SubjectId)
            .ToListAsync();
        var classIds = await _context.TeacherClasses
            .Where(tc => tc.TeacherId == teacher.Id)
            .Select(tc => tc.ClassId)
            .ToListAsync();

        model.Subjects = await _context.Subjects
            .Where(s => subjectIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .Select(s => new TeacherDashboardItem { Id = s.Id, Name = s.Name, Count = s.SubjectClasses.Count })
            .ToListAsync();

        model.Classes = await _context.Classes
            .Where(c => classIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new TeacherDashboardItem { Id = c.Id, Name = c.Name, Count = c.Students.Count(s => s.IsActive) })
            .ToListAsync();

        // Students in the teacher's grades (the same students the student directory shows them).
        var myStudents = _context.Students.Where(s => s.ClassId != null && classIds.Contains(s.ClassId.Value));
        model.ActiveStudents = await myStudents.CountAsync(s => s.IsActive);
        model.InactiveStudents = await myStudents.CountAsync(s => !s.IsActive);

        var today = DateTime.UtcNow.Date;
        var todaysAttendance = await _context.AttendanceRecords
            .Where(a => a.AttendanceDate == today && a.Student != null && a.Student.ClassId != null && classIds.Contains(a.Student.ClassId.Value))
            .Select(a => a.Status)
            .ToListAsync();
        model.MarkedToday = todaysAttendance.Count;
        model.PresentToday = todaysAttendance.Count(s => s == "Present");

        var exams = await _context.Exams
            .Include(e => e.Subject).Include(e => e.Class)
            .Where(e => e.TeacherId == teacher.Id)
            .ToListAsync();
        model.TotalExams = exams.Count;
        model.ExamsNeedingAction = exams.Count(e => e.Status == ExamStatus.Draft || e.Status == ExamStatus.Rejected);
        model.ExamsAwaitingApproval = exams.Count(e => e.Status == ExamStatus.PendingApproval);
        // Scheduled exams that haven't finished yet (exam times are school-local).
        var now = _clock.Now;
        model.UpcomingExams = exams
            .Where(e => e.StartDate != null && e.Status != ExamStatus.Rejected && !e.HasEnded(now)
                        && (e.EndDate != null || e.StartDate >= now.Date))
            .OrderBy(e => e.StartDate)
            .Take(5)
            .ToList();
        var examIds = exams.Select(e => e.Id).ToList();
        model.ExamAnswersToMark = await _context.ExamAnswers
            .CountAsync(a => a.IsCorrect == null && a.ExamSubmission != null && examIds.Contains(a.ExamSubmission.ExamId));

        var assignments = await _context.Assignments
            .Include(a => a.Subject)
            .Include(a => a.Submissions)
            .Where(a => a.SubjectId != null && subjectIds.Contains(a.SubjectId!.Value))
            .OrderByDescending(a => a.DueDate)
            .ToListAsync();

        model.TotalAssignments = assignments.Count;
        model.PendingAssignments = assignments.Count(a => a.IsPending);
        model.RecentAssignments = assignments.Take(5).ToList();

        model.TotalWeeklyChecklists = await _context.WeeklyChecklistItems.CountAsync();
        model.PendingWeeklyChecklists = await _context.WeeklyChecklistItems.CountAsync(w => w.Status != "Done");
        model.MyPendingWeeklyChecklists = await _context.WeeklyChecklistItems
            .CountAsync(w => w.Status != "Done" && w.AssignedTo == teacher.FirstName + " " + teacher.LastName);

        model.SubmissionsToGrade = await _context.AssignmentSubmissions
            .CountAsync(s => s.Status == "Submitted" && s.Assignment != null && s.Assignment.SubjectId != null && subjectIds.Contains(s.Assignment.SubjectId.Value));

        var gradesGiven = await _context.AssignmentSubmissions
            .Where(s => s.Grade != null && s.Assignment != null && s.Assignment.SubjectId != null && subjectIds.Contains(s.Assignment.SubjectId.Value))
            .Select(s => s.Grade!.Value)
            .ToListAsync();
        model.AverageGradeGiven = gradesGiven.Any() ? Math.Round(gradesGiven.Average(), 1) : (decimal?)null;

        return model;
    }

    private async Task<StudentDashboardViewModel> BuildStudentDashboardAsync()
    {
        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        var model = new StudentDashboardViewModel
        {
            StudentName = User.FindFirstValue("FullName") ?? "Student"
        };

        if (student == null)
        {
            return model;
        }

        model.ClassName = student.Class?.Name;

        var classSubjectIds = student.ClassId == null
            ? new List<int>()
            : await _context.SubjectClasses
                .Where(sc => sc.ClassId == student.ClassId)
                .Select(sc => sc.SubjectId)
                .ToListAsync();
        var individualSubjectIds = await _context.StudentSubjects
            .Where(ss => ss.StudentId == student.Id)
            .Select(ss => ss.SubjectId)
            .ToListAsync();

        model.Subjects = await _context.Subjects
            .Where(s => classSubjectIds.Union(individualSubjectIds).Contains(s.Id))
            .Select(s => s.Name)
            .ToListAsync();

        var submissions = await _context.AssignmentSubmissions
            .Include(s => s.Assignment).ThenInclude(a => a!.Subject)
            .Where(s => s.StudentId == student.Id)
            .OrderByDescending(s => s.Assignment!.DueDate)
            .ToListAsync();

        model.TotalAssignments = submissions.Count;
        model.PendingAssignments = submissions.Count(s => s.Status != "Graded");
        model.RecentSubmissions = submissions.Take(5).ToList();

        var gradedSubmissions = submissions.Where(s => s.Grade != null).Select(s => s.Grade!.Value).ToList();
        model.AverageGrade = gradedSubmissions.Any() ? Math.Round(gradedSubmissions.Average(), 1) : (decimal?)null;

        var attendance = await _context.AttendanceRecords
            .Where(a => a.StudentId == student.Id)
            .ToListAsync();

        model.TotalAttendanceRecords = attendance.Count;
        model.PresentCount = attendance.Count(a => a.Status == "Present");
        model.AttendanceRate = attendance.Count > 0
            ? Math.Round(model.PresentCount * 100m / attendance.Count, 1)
            : 0m;

        return model;
    }

    private async Task<ParentDashboardViewModel> BuildParentDashboardAsync()
    {
        var model = new ParentDashboardViewModel();

        var childIds = await _context.ParentStudents
            .Where(ps => ps.ParentUserId == CurrentUserId)
            .Select(ps => ps.StudentId)
            .Distinct()
            .ToListAsync();

        var children = await _context.Students
            .Include(s => s.Class)
            .Where(s => childIds.Contains(s.Id))
            .ToListAsync();

        foreach (var child in children)
        {
            var submissions = await _context.AssignmentSubmissions
                .Where(s => s.StudentId == child.Id)
                .ToListAsync();

            var pendingAssignments = submissions.Count(s => s.Status != "Graded");
            var gradedSubmissions = submissions.Where(s => s.Grade != null).Select(s => s.Grade!.Value).ToList();

            var attendance = await _context.AttendanceRecords
                .Where(a => a.StudentId == child.Id)
                .ToListAsync();

            var present = attendance.Count(a => a.Status == "Present");

            model.Children.Add(new ParentChildSummary
            {
                StudentId = child.Id,
                FullName = child.FullName,
                ClassName = child.Class?.Name,
                PendingAssignments = pendingAssignments,
                PresentCount = present,
                TotalAttendanceRecords = attendance.Count,
                AttendanceRate = attendance.Count > 0 ? Math.Round(present * 100m / attendance.Count, 1) : 0m,
                AverageGrade = gradedSubmissions.Any() ? Math.Round(gradedSubmissions.Average(), 1) : (decimal?)null
            });
        }

        return model;
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
