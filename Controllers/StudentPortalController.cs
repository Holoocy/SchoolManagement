using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Student")]
public class StudentPortalController : Controller
{
    private readonly ApplicationDbContext _context;

    public StudentPortalController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<List<int>> GetMySubjectIdsAsync(SchoolManagement.Models.Student student)
    {
        var classSubjectIds = student.ClassId == null
            ? new List<int>()
            : await _context.SubjectClasses.Where(sc => sc.ClassId == student.ClassId).Select(sc => sc.SubjectId).ToListAsync();

        var individualSubjectIds = await _context.StudentSubjects
            .Where(ss => ss.StudentId == student.Id)
            .Select(ss => ss.SubjectId)
            .ToListAsync();

        return classSubjectIds.Union(individualSubjectIds).ToList();
    }

    public async Task<IActionResult> MySubjects()
    {
        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return View(new List<Models.Subject>());

        var subjectIds = await GetMySubjectIdsAsync(student);

        var subjects = await _context.Subjects
            .Include(s => s.SubjectTeachers).ThenInclude(st => st.Teacher)
            .Where(s => subjectIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToListAsync();

        ViewBag.ClassName = student.Class?.Name;
        return View(subjects);
    }

    public async Task<IActionResult> Assignments()
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return View(new List<Models.AssignmentSubmission>());

        await EnsureGradeAssignmentsAsync(student);

        var submissions = await _context.AssignmentSubmissions
            .Include(s => s.Assignment).ThenInclude(a => a!.Subject)
            .Where(s => s.StudentId == student.Id)
            .OrderByDescending(s => s.Assignment!.DueDate)
            .ToListAsync();

        return View(submissions);
    }

    public async Task<IActionResult> Assignment(int id)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return Forbid();

        await EnsureGradeAssignmentsAsync(student);

        // Students can only open assignments given to them (individually or through their grade).
        var submission = await _context.AssignmentSubmissions
            .Include(s => s.Assignment).ThenInclude(a => a!.Subject)
            .Include(s => s.Assignment).ThenInclude(a => a!.Class)
            .FirstOrDefaultAsync(s => s.AssignmentId == id && s.StudentId == student.Id);
        if (submission == null) return NotFound();

        return View(submission);
    }

    // Assignments are given to a whole grade. Students who joined the grade after an
    // assignment was created get it the next time they open their assignments.
    private async Task EnsureGradeAssignmentsAsync(Student student)
    {
        if (student.ClassId == null) return;

        var missingAssignmentIds = await _context.Assignments
            .Where(a => a.ClassId == student.ClassId
                        && !a.Submissions.Any(s => s.StudentId == student.Id))
            .Select(a => a.Id)
            .ToListAsync();
        if (!missingAssignmentIds.Any()) return;

        foreach (var assignmentId in missingAssignmentIds)
        {
            _context.AssignmentSubmissions.Add(new AssignmentSubmission { AssignmentId = assignmentId, StudentId = student.Id });
        }
        await _context.SaveChangesAsync();
    }

    public async Task<IActionResult> Attendance()
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return View(new List<Models.AttendanceRecord>());

        var records = await _context.AttendanceRecords
            .Where(a => a.StudentId == student.Id)
            .OrderByDescending(a => a.AttendanceDate)
            .ToListAsync();

        ViewBag.PresentCount = records.Count(a => a.Status == "Present");
        ViewBag.AttendanceRate = records.Count > 0
            ? Math.Round(records.Count(a => a.Status == "Present") * 100m / records.Count, 1)
            : 0m;

        return View(records);
    }

    public async Task<IActionResult> Profile()
    {
        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return NotFound();

        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string phone, string address)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return NotFound();

        student.Phone = phone;
        student.Address = address;
        await _context.SaveChangesAsync();

        TempData["ProfileSuccess"] = "Your profile has been updated.";
        return RedirectToAction(nameof(Profile));
    }

    public async Task<IActionResult> Exams()
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return View(new List<Exam>());

        var exams = await _context.Exams
            .Include(e => e.Subject)
            .Where(e => e.ClassId == student.ClassId && e.Status == ExamStatus.Published)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        var examIds = exams.Select(e => e.Id).ToList();
        var mySubmissions = await _context.ExamSubmissions
            .Include(s => s.Answers)
            .Where(s => s.StudentId == student.Id && examIds.Contains(s.ExamId))
            .ToListAsync();

        ViewBag.Submissions = mySubmissions.ToDictionary(s => s.ExamId);
        return View(exams);
    }

    public async Task<IActionResult> TakeExam(int examId)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return Forbid();

        var exam = await _context.Exams.Include(e => e.Questions).Include(e => e.Subject)
            .FirstOrDefaultAsync(e => e.Id == examId && e.ClassId == student.ClassId && e.Status == ExamStatus.Published);
        if (exam == null) return NotFound();

        var alreadySubmitted = await _context.ExamSubmissions.AnyAsync(s => s.ExamId == examId && s.StudentId == student.Id);
        if (alreadySubmitted)
        {
            return RedirectToAction(nameof(ExamResult), new { examId });
        }

        return View(exam);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TakeExam(int examId, Dictionary<int, string> answers)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return Forbid();

        var exam = await _context.Exams.Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == examId && e.ClassId == student.ClassId && e.Status == ExamStatus.Published);
        if (exam == null) return NotFound();

        var alreadySubmitted = await _context.ExamSubmissions.AnyAsync(s => s.ExamId == examId && s.StudentId == student.Id);
        if (alreadySubmitted)
        {
            return RedirectToAction(nameof(ExamResult), new { examId });
        }

        answers ??= new Dictionary<int, string>();

        // Store every answer. Objective questions are marked now; written answers to
        // uploaded questions stay unmarked (null) until the teacher marks them.
        var examAnswers = exam.Questions.Select(q =>
        {
            answers.TryGetValue(q.Id, out var answer);
            answer = answer?.Trim() ?? string.Empty;
            return new ExamAnswer
            {
                ExamQuestionId = q.Id,
                AnswerText = answer,
                IsCorrect = q.IsManuallyMarked && answer.Length > 0 ? null : q.IsCorrect(answer)
            };
        }).ToList();

        _context.ExamSubmissions.Add(new ExamSubmission
        {
            ExamId = examId,
            StudentId = student.Id,
            Score = examAnswers.Count(a => a.IsCorrect == true),
            TotalQuestions = exam.Questions.Count,
            Answers = examAnswers
        });
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(ExamResult), new { examId });
    }

    public async Task<IActionResult> ExamResult(int examId)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (student == null) return Forbid();

        var submission = await _context.ExamSubmissions
            .Include(s => s.Exam).ThenInclude(e => e!.Subject)
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.ExamId == examId && s.StudentId == student.Id);
        if (submission == null) return NotFound();

        return View(submission);
    }
}
