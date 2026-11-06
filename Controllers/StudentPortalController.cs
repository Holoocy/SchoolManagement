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

        var submissions = await _context.AssignmentSubmissions
            .Include(s => s.Assignment).ThenInclude(a => a!.Subject)
            .Where(s => s.StudentId == student.Id)
            .OrderByDescending(s => s.Assignment!.DueDate)
            .ToListAsync();

        return View(submissions);
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
        var score = exam.Questions.Count(q => answers.TryGetValue(q.Id, out var chosen) && chosen == q.CorrectOption);

        _context.ExamSubmissions.Add(new ExamSubmission
        {
            ExamId = examId,
            StudentId = student.Id,
            Score = score,
            TotalQuestions = exam.Questions.Count
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
            .FirstOrDefaultAsync(s => s.ExamId == examId && s.StudentId == student.Id);
        if (submission == null) return NotFound();

        return View(submission);
    }
}
