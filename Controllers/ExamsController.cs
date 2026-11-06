using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin,Teacher")]
public class ExamsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ExamsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<Teacher?> GetCurrentTeacherAsync() =>
        await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);

    // ---------- Teacher: manage own exams ----------

    public async Task<IActionResult> Index()
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return View(new List<Exam>());

        var exams = await _context.Exams
            .Include(e => e.Subject).Include(e => e.Class).Include(e => e.Questions)
            .Where(e => e.TeacherId == teacher.Id)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return View(exams);
    }

    public async Task<IActionResult> Create()
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return Forbid();

        await PopulateSubjectsAndClassesAsync(teacher.Id);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Exam exam)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return Forbid();

        var ownsSubject = await _context.SubjectTeachers.AnyAsync(st => st.TeacherId == teacher.Id && st.SubjectId == exam.SubjectId);
        var ownsClass = await _context.TeacherClasses.AnyAsync(tc => tc.TeacherId == teacher.Id && tc.ClassId == exam.ClassId);
        if (!ownsSubject) ModelState.AddModelError(nameof(Exam.SubjectId), "Select a subject assigned to you.");
        if (!ownsClass) ModelState.AddModelError(nameof(Exam.ClassId), "Select a grade assigned to you.");

        ModelState.Remove(nameof(Exam.Status));
        ModelState.Remove(nameof(Exam.TeacherId));

        if (!ModelState.IsValid)
        {
            await PopulateSubjectsAndClassesAsync(teacher.Id, exam.SubjectId, exam.ClassId);
            return View(exam);
        }

        exam.TeacherId = teacher.Id;
        exam.Status = ExamStatus.Draft;

        _context.Add(exam);
        await _context.SaveChangesAsync();
        TempData["ExamSuccess"] = $"\"{exam.Title}\" created. Add questions, then submit it for approval.";
        return RedirectToAction(nameof(Questions), new { examId = exam.Id });
    }

    private async Task<Exam?> GetOwnExamAsync(int examId)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return null;

        return await _context.Exams
            .Include(e => e.Subject).Include(e => e.Class).Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == examId && e.TeacherId == teacher.Id);
    }

    public async Task<IActionResult> Questions(int examId)
    {
        var exam = await GetOwnExamAsync(examId);
        if (exam == null) return NotFound();

        return View(exam);
    }

    public async Task<IActionResult> AddQuestion(int examId)
    {
        var exam = await GetOwnExamAsync(examId);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Draft)
        {
            TempData["ExamError"] = "You can only add questions while the exam is still in draft.";
            return RedirectToAction(nameof(Questions), new { examId });
        }

        ViewBag.Exam = exam;
        return View(new ExamQuestion { ExamId = examId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(ExamQuestion question)
    {
        var exam = await GetOwnExamAsync(question.ExamId);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Draft) return Forbid();

        ModelState.Remove(nameof(ExamQuestion.Exam));
        if (!ModelState.IsValid)
        {
            ViewBag.Exam = exam;
            return View(question);
        }

        _context.Add(question);
        await _context.SaveChangesAsync();
        TempData["ExamSuccess"] = "Question added.";
        return RedirectToAction(nameof(Questions), new { examId = exam.Id });
    }

    public async Task<IActionResult> EditQuestion(int id)
    {
        var question = await _context.ExamQuestions.Include(q => q.Exam).FirstOrDefaultAsync(q => q.Id == id);
        if (question?.Exam == null) return NotFound();

        var exam = await GetOwnExamAsync(question.ExamId);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Draft)
        {
            TempData["ExamError"] = "You can only edit questions while the exam is still in draft.";
            return RedirectToAction(nameof(Questions), new { examId = exam.Id });
        }

        ViewBag.Exam = exam;
        return View(question);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuestion(int id, ExamQuestion question)
    {
        if (id != question.Id) return NotFound();

        var exam = await GetOwnExamAsync(question.ExamId);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Draft) return Forbid();

        ModelState.Remove(nameof(ExamQuestion.Exam));
        if (!ModelState.IsValid)
        {
            ViewBag.Exam = exam;
            return View(question);
        }

        _context.Update(question);
        await _context.SaveChangesAsync();
        TempData["ExamSuccess"] = "Question updated.";
        return RedirectToAction(nameof(Questions), new { examId = exam.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id, int examId)
    {
        var exam = await GetOwnExamAsync(examId);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Draft) return Forbid();

        var question = await _context.ExamQuestions.FirstOrDefaultAsync(q => q.Id == id && q.ExamId == examId);
        if (question != null)
        {
            _context.ExamQuestions.Remove(question);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Questions), new { examId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitForApproval(int id)
    {
        var exam = await GetOwnExamAsync(id);
        if (exam == null) return NotFound();

        if (exam.Status != ExamStatus.Draft && exam.Status != ExamStatus.Rejected)
        {
            TempData["ExamError"] = "This exam has already been submitted.";
            return RedirectToAction(nameof(Index));
        }

        if (!exam.Questions.Any())
        {
            TempData["ExamError"] = "Add at least one question before submitting for approval.";
            return RedirectToAction(nameof(Questions), new { examId = id });
        }

        exam.Status = ExamStatus.PendingApproval;
        exam.RejectionReason = null;
        await _context.SaveChangesAsync();

        TempData["ExamSuccess"] = "Sent to Admin for approval.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id)
    {
        var exam = await GetOwnExamAsync(id);
        if (exam == null) return NotFound();

        if (exam.Status != ExamStatus.Approved)
        {
            TempData["ExamError"] = "This exam must be approved by an Admin first.";
            return RedirectToAction(nameof(Index));
        }

        exam.Status = ExamStatus.Published;
        await _context.SaveChangesAsync();

        TempData["ExamSuccess"] = "Exam sent to students.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Results(int id)
    {
        Exam? exam;
        if (User.IsInRole("Admin"))
        {
            exam = await _context.Exams.Include(e => e.Subject).Include(e => e.Class)
                .FirstOrDefaultAsync(e => e.Id == id);
        }
        else
        {
            exam = await GetOwnExamAsync(id);
        }

        if (exam == null) return NotFound();

        var submissions = await _context.ExamSubmissions
            .Include(s => s.Student)
            .Where(s => s.ExamId == id)
            .OrderByDescending(s => s.Score)
            .ToListAsync();

        ViewBag.Exam = exam;
        return View(submissions);
    }

    public async Task<IActionResult> Notify(int id)
    {
        var exam = await GetOwnExamAsync(id);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Approved && exam.Status != ExamStatus.Published)
        {
            TempData["ExamError"] = "The exam must be approved before you can notify students and parents.";
            return RedirectToAction(nameof(Index));
        }

        return View(exam);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Notify(int id, DateTime startDate)
    {
        var exam = await GetOwnExamAsync(id);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Approved && exam.Status != ExamStatus.Published)
        {
            return Forbid();
        }

        exam.StartDate = startDate;

        var senderName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Teacher";
        var subjectLine = $"Exam Notice: {exam.Title}";
        var body = $"The exam \"{exam.Title}\" ({exam.Subject!.Name}) is scheduled to start on {startDate:MMMM d, yyyy}.";

        var studentUserIds = await _context.Students
            .Where(s => s.ClassId == exam.ClassId && s.UserId != null)
            .Select(s => s.UserId!.Value)
            .Distinct().ToListAsync();

        var studentIdsInClass = await _context.Students
            .Where(s => s.ClassId == exam.ClassId)
            .Select(s => s.Id).ToListAsync();
        var parentUserIds = await _context.ParentStudents
            .Where(ps => studentIdsInClass.Contains(ps.StudentId))
            .Select(ps => ps.ParentUserId).Distinct().ToListAsync();

        if (studentUserIds.Any())
        {
            _context.Messages.Add(new Message
            {
                SenderUserId = CurrentUserId,
                SenderName = senderName,
                SenderRole = "Teacher",
                Audience = MessageAudience.Student,
                Scope = MessageScope.Class,
                ClassId = exam.ClassId,
                Subject = subjectLine,
                Body = body,
                Recipients = studentUserIds.Select(uid => new MessageRecipient { RecipientUserId = uid }).ToList()
            });
        }

        if (parentUserIds.Any())
        {
            _context.Messages.Add(new Message
            {
                SenderUserId = CurrentUserId,
                SenderName = senderName,
                SenderRole = "Teacher",
                Audience = MessageAudience.Parent,
                Scope = MessageScope.Class,
                ClassId = exam.ClassId,
                Subject = subjectLine,
                Body = body,
                Recipients = parentUserIds.Select(uid => new MessageRecipient { RecipientUserId = uid }).ToList()
            });
        }

        await _context.SaveChangesAsync();
        TempData["ExamSuccess"] = "Start date notification sent to students and parents.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateSubjectsAndClassesAsync(int teacherId, int? selectedSubjectId = null, int? selectedClassId = null)
    {
        var subjectIds = await _context.SubjectTeachers.Where(st => st.TeacherId == teacherId).Select(st => st.SubjectId).ToListAsync();
        var classIds = await _context.TeacherClasses.Where(tc => tc.TeacherId == teacherId).Select(tc => tc.ClassId).ToListAsync();

        ViewBag.Subjects = new SelectList(
            await _context.Subjects.Where(s => subjectIds.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync(),
            "Id", "Name", selectedSubjectId);
        ViewBag.Classes = new SelectList(
            await _context.Classes.Where(c => classIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(),
            "Id", "Name", selectedClassId);
    }

    // ---------- Admin: approval ----------

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PendingApproval()
    {
        var exams = await _context.Exams
            .Include(e => e.Subject).Include(e => e.Class).Include(e => e.Teacher).Include(e => e.Questions)
            .Where(e => e.Status == ExamStatus.PendingApproval)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        return View(exams);
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ReviewDetails(int id)
    {
        var exam = await _context.Exams
            .Include(e => e.Subject).Include(e => e.Class).Include(e => e.Teacher).Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (exam == null) return NotFound();

        return View(exam);
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AllExams()
    {
        var exams = await _context.Exams
            .Include(e => e.Subject).Include(e => e.Class).Include(e => e.Teacher)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return View(exams);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var exam = await _context.Exams.FindAsync(id);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.PendingApproval) return RedirectToAction(nameof(PendingApproval));

        exam.Status = ExamStatus.Approved;
        exam.RejectionReason = null;
        await _context.SaveChangesAsync();

        TempData["ExamSuccess"] = "Exam approved.";
        return RedirectToAction(nameof(PendingApproval));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var exam = await _context.Exams.FindAsync(id);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.PendingApproval) return RedirectToAction(nameof(PendingApproval));

        exam.Status = ExamStatus.Rejected;
        exam.RejectionReason = string.IsNullOrWhiteSpace(reason) ? "No reason given." : reason.Trim();
        await _context.SaveChangesAsync();

        TempData["ExamSuccess"] = "Exam rejected.";
        return RedirectToAction(nameof(PendingApproval));
    }
}
