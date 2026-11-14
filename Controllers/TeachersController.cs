using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin")]
public class TeachersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ResultsService _results;

    public TeachersController(ApplicationDbContext context, ResultsService results)
    {
        _context = context;
        _results = results;
    }

    public async Task<IActionResult> Index()
    {
        var teachers = await _context.Teachers
            .OrderBy(t => t.LastName)
            .ThenBy(t => t.FirstName)
            .ToListAsync();

        return View(teachers);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Teacher teacher)
    {
        teacher.EmployeeId = await GenerateEmployeeIdAsync();
        ModelState.Remove(nameof(Teacher.EmployeeId));

        if (!ModelState.IsValid)
        {
            return View(teacher);
        }

        _context.Add(teacher);
        await _context.SaveChangesAsync();
        TempData["TeacherSuccess"] = $"{teacher.FullName} added with employee ID {teacher.EmployeeId}.";
        return RedirectToAction(nameof(Index));
    }

    // Everything about a teacher and their work: grades, subjects, students, assignments,
    // exams and CA tests, results, attendance, checklist and messages.
    public async Task<IActionResult> Details(int? id, string? term)
    {
        if (id == null)
        {
            return NotFound();
        }

        var teacher = await _context.Teachers.FirstOrDefaultAsync(m => m.Id == id);
        if (teacher == null)
        {
            return NotFound();
        }

        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        var model = new TeacherProfileViewModel
        {
            Teacher = teacher,
            Scheme = scheme,
            Terms = terms,
            Term = ResultsService.ResolveTerm(term, terms, scheme),
            Account = teacher.UserId == null ? null : await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == teacher.UserId)
        };

        var classIds = await _context.TeacherClasses.Where(tc => tc.TeacherId == id).Select(tc => tc.ClassId).ToListAsync();
        var subjectIds = await _context.SubjectTeachers.Where(st => st.TeacherId == id).Select(st => st.SubjectId).ToListAsync();

        model.Grades = await _context.Classes.Where(c => classIds.Contains(c.Id)).OrderBy(c => c.Name)
            .Select(c => new TeacherDashboardItem { Id = c.Id, Name = c.Name, Count = c.Students.Count(s => s.IsActive) })
            .ToListAsync();
        model.ActiveStudents = model.Grades.Sum(g => g.Count);
        model.Subjects = (await _context.Subjects.Where(s => subjectIds.Contains(s.Id)).OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Name, s.Code, Grades = s.SubjectClasses.Select(sc => sc.Class!.Name).ToList() })
                .ToListAsync())
            .Select(s => new TeacherSubjectLine { Id = s.Id, Name = s.Name, Code = s.Code, Grades = s.Grades.OrderBy(g => g).ToList() })
            .ToList();

        // Assignments in the teacher's subjects for the term.
        var assignments = await _context.Assignments
            .Include(a => a.Subject).Include(a => a.Class).Include(a => a.Submissions)
            .Where(a => a.SubjectId != null && subjectIds.Contains(a.SubjectId.Value) && a.Term == model.Term)
            .OrderByDescending(a => a.DueDate)
            .ToListAsync();
        model.Assignments = assignments.Select(a =>
        {
            var graded = a.Submissions.Where(s => s.Grade != null && a.MaxScore > 0).ToList();
            return new TeacherAssignmentLine
            {
                Assignment = a,
                Assigned = a.Submissions.Count,
                Graded = graded.Count,
                AveragePercent = graded.Any() ? Math.Round(graded.Average(s => s.Grade!.Value * 100m / a.MaxScore), 1) : null
            };
        }).ToList();
        model.SubmissionsToGrade = assignments.Sum(a => a.Submissions.Count(s => s.Status == "Submitted"));

        // Exams and CA tests the teacher created for the term.
        var exams = await _context.Exams
            .Include(e => e.Subject).Include(e => e.Class).Include(e => e.Questions)
            .Include(e => e.Submissions).ThenInclude(s => s.Answers)
            .Where(e => e.TeacherId == id && e.Term == model.Term)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
        model.Exams = exams.Select(e =>
        {
            var marked = e.Submissions.Where(s => !s.AwaitingMarking).ToList();
            return new TeacherExamLine
            {
                Exam = e,
                Questions = e.Questions.Count,
                Submissions = e.Submissions.Count,
                AwaitingMarking = e.Submissions.Count(s => s.AwaitingMarking),
                AveragePercent = marked.Any() ? Math.Round(marked.Average(s => s.Percentage), 1) : null
            };
        }).ToList();
        model.ExamAnswersToMark = exams.Sum(e => e.Submissions.Sum(s => s.Answers.Count(a => a.IsCorrect == null)));

        // Results for each grade and subject the teacher has.
        var students = await _context.Students.Where(s => s.IsActive && s.ClassId != null && classIds.Contains(s.ClassId.Value))
            .Select(s => new { s.Id, ClassId = s.ClassId!.Value }).ToListAsync();
        var results = await _results.ComputeAsync(students.Select(s => s.Id).ToList(), subjectIds, model.Term, scheme);
        foreach (var grade in model.Grades)
        {
            var inGrade = students.Where(s => s.ClassId == grade.Id).Select(s => s.Id).ToList();
            foreach (var subject in model.Subjects)
            {
                var totals = inGrade.Select(sid => results.TryGetValue((sid, subject.Id), out var r) ? r.Total : null)
                    .Where(t => t != null).Select(t => t!.Value).ToList();
                model.Results.Add(new TeacherResultLine
                {
                    ClassId = grade.Id, ClassName = grade.Name, SubjectId = subject.Id, SubjectName = subject.Name,
                    Students = inGrade.Count, WithScores = totals.Count,
                    Average = totals.Any() ? scheme.Round(totals.Average()) : null
                });
            }
        }
        if (teacher.UserId != null)
        {
            model.ScoresRecordedThisTerm = await _context.SubjectScores.CountAsync(s => s.Term == model.Term && s.UpdatedByUserId == teacher.UserId);
            model.MessagesSent = await _context.Messages.CountAsync(m => m.SenderUserId == teacher.UserId);
            model.LastMessageAt = await _context.Messages.Where(m => m.SenderUserId == teacher.UserId).MaxAsync(m => (DateTime?)m.SentAt);
        }

        // Attendance in the teacher's grades over the last 30 days.
        var since = DateTime.UtcNow.Date.AddDays(-29);
        var studentIds = students.Select(s => s.Id).ToList();
        var attendance = await _context.AttendanceRecords
            .Where(a => a.AttendanceDate >= since && studentIds.Contains(a.StudentId))
            .Select(a => new { a.AttendanceDate, a.Status }).ToListAsync();
        model.AttendanceRecords30Days = attendance.Count;
        model.AttendanceDaysTaken30Days = attendance.Select(a => a.AttendanceDate.Date).Distinct().Count();
        model.AttendanceRate30Days = attendance.Any() ? Math.Round(attendance.Count(a => a.Status == "Present") * 100m / attendance.Count, 1) : null;

        var fullName = teacher.FirstName + " " + teacher.LastName;
        model.ChecklistPending = await _context.WeeklyChecklistItems.CountAsync(w => w.AssignedTo == fullName && w.Status != "Done");
        model.ChecklistDone = await _context.WeeklyChecklistItems.CountAsync(w => w.AssignedTo == fullName && w.Status == "Done");

        return View(model);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null)
        {
            return NotFound();
        }

        return View(teacher);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Teacher teacher)
    {
        if (id != teacher.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(teacher);
        }

        try
        {
            var existing = await _context.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            teacher.UserId = existing?.UserId;

            _context.Update(teacher);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!TeacherExists(teacher.Id))
            {
                return NotFound();
            }
            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var teacher = await _context.Teachers.FirstOrDefaultAsync(m => m.Id == id);
        if (teacher == null)
        {
            return NotFound();
        }

        return View(teacher);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher != null)
        {
            _context.Teachers.Remove(teacher);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> AssignSubjects(int? id)
    {
        if (id == null) return NotFound();

        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        var assignedIds = await _context.SubjectTeachers
            .Where(st => st.TeacherId == id)
            .Select(st => st.SubjectId)
            .ToListAsync();

        ViewBag.Teacher = teacher;
        ViewBag.AssignedIds = assignedIds;
        var subjects = await _context.Subjects.OrderBy(s => s.Name).ToListAsync();
        return View(subjects);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignSubjects(int id, List<int>? selectedIds)
    {
        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        selectedIds ??= new List<int>();

        var existingLinks = await _context.SubjectTeachers.Where(st => st.TeacherId == id).ToListAsync();
        _context.SubjectTeachers.RemoveRange(existingLinks.Where(l => !selectedIds.Contains(l.SubjectId)));

        var existingSubjectIds = existingLinks.Select(l => l.SubjectId).ToHashSet();
        foreach (var subjectId in selectedIds.Where(sid => !existingSubjectIds.Contains(sid)))
        {
            _context.SubjectTeachers.Add(new SubjectTeacher { TeacherId = id, SubjectId = subjectId });
        }

        await _context.SaveChangesAsync();
        TempData["TeacherSuccess"] = $"Subjects updated for {teacher.FullName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> AssignClasses(int? id)
    {
        if (id == null) return NotFound();

        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        var assignedIds = await _context.TeacherClasses
            .Where(tc => tc.TeacherId == id)
            .Select(tc => tc.ClassId)
            .ToListAsync();

        ViewBag.Teacher = teacher;
        ViewBag.AssignedIds = assignedIds;
        var classes = await _context.Classes.OrderBy(c => c.Name).ToListAsync();
        return View(classes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignClasses(int id, List<int>? selectedIds)
    {
        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        selectedIds ??= new List<int>();

        var existingLinks = await _context.TeacherClasses.Where(tc => tc.TeacherId == id).ToListAsync();
        _context.TeacherClasses.RemoveRange(existingLinks.Where(l => !selectedIds.Contains(l.ClassId)));

        var existingClassIds = existingLinks.Select(l => l.ClassId).ToHashSet();
        foreach (var classId in selectedIds.Where(cid => !existingClassIds.Contains(cid)))
        {
            _context.TeacherClasses.Add(new TeacherClass { TeacherId = id, ClassId = classId });
        }

        await _context.SaveChangesAsync();
        TempData["TeacherSuccess"] = $"Classes updated for {teacher.FullName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private bool TeacherExists(int id)
    {
        return _context.Teachers.Any(e => e.Id == id);
    }

    private async Task<string> GenerateEmployeeIdAsync()
    {
        var existingIds = await _context.Teachers
            .Where(t => t.EmployeeId.StartsWith("EMP"))
            .Select(t => t.EmployeeId)
            .ToListAsync();

        var maxSuffix = existingIds
            .Select(id => int.TryParse(id.Substring(3), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"EMP{(maxSuffix + 1):D4}";
    }
}
