using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

// Admins see the whole school; teachers see their own grades and subjects.
[Authorize(Roles = "Admin,Teacher")]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ResultsService _results;

    public ReportsController(ApplicationDbContext context, ResultsService results)
    {
        _context = context;
        _results = results;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    public async Task<IActionResult> Index(string? term)
    {
        var scope = await _results.GetScopeAsync(CurrentUserId, IsAdmin);
        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        term = ResultsService.ResolveTerm(term, terms, scheme);
        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);

        // Teachers: students in their grades, their subjects and assignments.
        var students = scope.IsAdmin ? _context.Students : _context.Students.Where(s => s.ClassId != null && scope.ClassIds.Contains(s.ClassId.Value));
        var assignments = scope.IsAdmin
            ? _context.Assignments
            : _context.Assignments.Where(a => a.SubjectId != null && scope.SubjectIds.Contains(a.SubjectId.Value));
        var studentIds = await students.Select(s => s.Id).ToListAsync();

        var model = new ReportsViewModel
        {
            IsTeacher = !scope.IsAdmin,
            HasTeacherProfile = scope.IsAdmin || scope.Teacher != null,
            Scheme = scheme,
            Term = term,
            Terms = terms,
            TotalStudents = studentIds.Count,
            TotalTeachers = scope.IsAdmin ? await _context.Teachers.CountAsync() : scope.ClassIds.Count,
            TotalSubjects = scope.SubjectIds.Count,
            TotalAssignments = await assignments.CountAsync()
        };

        var assignmentStatuses = (await assignments.Include(a => a.Submissions).ToListAsync())
            .GroupBy(a => a.ProgressStatus)
            .Select(g => new StatusBreakdown { Status = g.Key, Count = g.Count() })
            .OrderBy(b => b.Status)
            .ToList();
        model.AssignmentStatusBreakdown = assignmentStatuses;

        var checklist = scope.IsAdmin || scope.Teacher == null
            ? _context.WeeklyChecklistItems
            : _context.WeeklyChecklistItems.Where(w => w.AssignedTo == scope.Teacher.FirstName + " " + scope.Teacher.LastName);
        model.TotalWeeklyChecklists = await checklist.CountAsync();
        model.WeeklyChecklistStatusBreakdown = await checklist
            .GroupBy(w => w.Status)
            .Select(g => new StatusBreakdown { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var recentAttendance = await _context.AttendanceRecords
            .Where(a => a.AttendanceDate.Date >= sevenDaysAgo && studentIds.Contains(a.StudentId))
            .Select(a => new { a.AttendanceDate, a.Status })
            .ToListAsync();

        model.AttendanceLastSevenDays = Enumerable.Range(0, 7)
            .Select(offset => sevenDaysAgo.AddDays(offset))
            .Select(date => new AttendanceDaySummary
            {
                Date = date,
                Total = recentAttendance.Count(a => a.AttendanceDate.Date == date),
                Present = recentAttendance.Count(a => a.AttendanceDate.Date == date && a.Status == "Present")
            })
            .ToList();

        var totalRecords = recentAttendance.Count;
        var totalPresent = recentAttendance.Count(a => a.Status == "Present");
        model.OverallAttendanceRate = totalRecords > 0 ? Math.Round(totalPresent * 100m / totalRecords, 1) : 0m;

        // Results by grade for the term.
        var classes = await _context.Classes.Where(c => scope.ClassIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync();
        var gradeStudents = await _context.Students.Where(s => s.IsActive && s.ClassId != null && scope.ClassIds.Contains(s.ClassId.Value))
            .Select(s => new { s.Id, s.ClassId, Name = s.FirstName + " " + s.LastName }).ToListAsync();
        var results = await _results.ComputeAsync(gradeStudents.Select(s => s.Id).ToList(), scope.IsAdmin ? null : scope.SubjectIds, term, scheme);
        foreach (var c in classes)
        {
            var averages = gradeStudents.Where(s => s.ClassId == c.Id)
                .Select(s => (s.Name, Avg: results.Values.Where(r => r.StudentId == s.Id && r.Total != null).Select(r => r.Total!.Value).DefaultIfEmpty().Average()
                                         , Has: results.Values.Any(r => r.StudentId == s.Id && r.Total != null)))
                .ToList();
            var scored = averages.Where(a => a.Has).ToList();
            model.Grades.Add(new GradeResultSummary
            {
                ClassId = c.Id,
                ClassName = c.Name,
                Students = averages.Count,
                StudentsWithScores = scored.Count,
                Average = scored.Any() ? scheme.Round(scored.Average(a => a.Avg)) : null,
                TopStudent = scored.OrderByDescending(a => a.Avg).Select(a => $"{a.Name} ({scheme.Format(scheme.Round(a.Avg))})").FirstOrDefault()
            });
        }

        return View(model);
    }

    // Broadsheet: a grade's students with each subject's total (or one subject's
    // Assignment, CA and Exam), averages and positions.
    public async Task<IActionResult> Results(int? classId, int? subjectId, string? term)
    {
        var scope = await _results.GetScopeAsync(CurrentUserId, IsAdmin);
        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        var classes = await _context.Classes.Where(c => scope.ClassIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync();
        var subjectOptions = await _context.Subjects.Where(s => scope.SubjectIds.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync();

        if (classId == null && classes.Count == 1) classId = classes[0].Id;
        if (classId != null && !scope.ClassIds.Contains(classId.Value)) classId = null;
        if (subjectId != null && !scope.SubjectIds.Contains(subjectId.Value)) subjectId = null;

        var model = new ResultsReportViewModel
        {
            Scheme = scheme,
            Terms = terms,
            Term = ResultsService.ResolveTerm(term, terms, scheme),
            Classes = classes,
            SubjectOptions = subjectOptions,
            ClassId = classId,
            SubjectId = subjectId,
            IsAdmin = scope.IsAdmin,
            ClassName = classes.FirstOrDefault(c => c.Id == classId)?.Name,
            SubjectName = subjectOptions.FirstOrDefault(s => s.Id == subjectId)?.Name
        };
        if (classId == null) return View(model);

        var students = await _context.Students.Where(s => s.ClassId == classId && s.IsActive)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync();
        var studentIds = students.Select(s => s.Id).ToList();
        IReadOnlyCollection<int> subjectFilter = subjectId != null ? new List<int> { subjectId.Value } : scope.SubjectIds;
        var results = await _results.ComputeAsync(studentIds, subjectFilter, model.Term, scheme);

        // Columns: subjects linked to the grade plus any subject with scores (teachers: only theirs).
        if (subjectId != null)
        {
            model.Columns = subjectOptions.Where(s => s.Id == subjectId).ToList();
        }
        else
        {
            var linked = await _context.SubjectClasses.Where(sc => sc.ClassId == classId).Select(sc => sc.SubjectId).ToListAsync();
            var withScores = results.Keys.Select(k => k.SubjectId).ToHashSet();
            model.Columns = subjectOptions.Where(s => linked.Contains(s.Id) || withScores.Contains(s.Id)).ToList();
        }

        foreach (var student in students)
        {
            var row = new ResultsReportRow { Student = student };
            foreach (var subject in model.Columns)
            {
                if (results.TryGetValue((student.Id, subject.Id), out var r)) row.Results[subject.Id] = r;
            }
            var totals = row.Results.Values.Where(r => r.Total != null).Select(r => r.Total!.Value).ToList();
            row.Average = totals.Any() ? scheme.Round(totals.Average()) : null;
            row.Total = subjectId != null && row.Results.TryGetValue(subjectId.Value, out var only) ? only.Total : null;
            model.Rows.Add(row);
        }

        var ranks = ResultsService.Rank(model.Rows.Select(r => (r.Student.Id, subjectId != null ? r.Total : r.Average)));
        foreach (var row in model.Rows) row.Position = ranks.TryGetValue(row.Student.Id, out var p) ? p : null;
        model.Rows = model.Rows.OrderBy(r => r.Position ?? int.MaxValue).ThenBy(r => r.Student.LastName).ToList();

        foreach (var subject in model.Columns)
        {
            var totals = model.Rows.Select(r => r.Results.TryGetValue(subject.Id, out var x) ? x.Total : null).Where(t => t != null).Select(t => t!.Value).ToList();
            model.SubjectAverages[subject.Id] = totals.Any() ? scheme.Round(totals.Average()) : null;
        }

        return View(model);
    }

    // One student's full record (Admins: anyone; teachers: students in their grades).
    public async Task<IActionResult> Student(int id, string? term)
    {
        var scope = await _results.GetScopeAsync(CurrentUserId, IsAdmin);
        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.Id == id);
        if (student == null || (!scope.IsAdmin && (student.ClassId == null || !scope.ClassIds.Contains(student.ClassId.Value))))
            return NotFound();

        var record = await _results.BuildStudentRecordAsync(student, term);
        record.ReturnController = "Reports";
        record.ReturnAction = nameof(Student);
        return View(record);
    }
}
