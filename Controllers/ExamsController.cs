using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin,Teacher")]
public class ExamsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ISchoolClock _clock;

    private const long MaxAttachmentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedAttachmentExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf" };

    public ExamsController(ApplicationDbContext context, IWebHostEnvironment environment, ISchoolClock clock)
    {
        _context = context;
        _environment = environment;
        _clock = clock;
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

        ValidateSchedule(exam.StartDate, exam.DurationMinutes, mustBeFuture: true);

        ModelState.Remove(nameof(Exam.Status));
        ModelState.Remove(nameof(Exam.TeacherId));

        if (!ModelState.IsValid)
        {
            await PopulateSubjectsAndClassesAsync(teacher.Id, exam.SubjectId, exam.ClassId);
            return View(exam);
        }

        exam.TeacherId = teacher.Id;
        exam.Status = ExamStatus.Draft;
        exam.ReminderSentAt = null;

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

    // Teachers can change an exam while it is a draft, or after an Admin rejects it
    // (so they can fix it and submit it for approval again).
    private static bool IsEditable(Exam exam) =>
        exam.Status == ExamStatus.Draft || exam.Status == ExamStatus.Rejected;

    public async Task<IActionResult> Edit(int id)
    {
        var exam = await GetOwnExamAsync(id);
        if (exam == null) return NotFound();
        if (!IsEditable(exam))
        {
            TempData["ExamError"] = "You can only edit an exam while it is a draft or has been rejected.";
            return RedirectToAction(nameof(Index));
        }

        await PopulateSubjectsAndClassesAsync(exam.TeacherId, exam.SubjectId, exam.ClassId);
        return View(exam);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string title, int subjectId, int classId, DateTime? startDate, int? durationMinutes)
    {
        var exam = await GetOwnExamAsync(id);
        if (exam == null) return NotFound();
        if (!IsEditable(exam)) return Forbid();

        var ownsSubject = await _context.SubjectTeachers.AnyAsync(st => st.TeacherId == exam.TeacherId && st.SubjectId == subjectId);
        var ownsClass = await _context.TeacherClasses.AnyAsync(tc => tc.TeacherId == exam.TeacherId && tc.ClassId == classId);
        if (string.IsNullOrWhiteSpace(title)) ModelState.AddModelError(nameof(Exam.Title), "The Title field is required.");
        if (!ownsSubject) ModelState.AddModelError(nameof(Exam.SubjectId), "Select a subject assigned to you.");
        if (!ownsClass) ModelState.AddModelError(nameof(Exam.ClassId), "Select a grade assigned to you.");
        var scheduleChanged = startDate != exam.StartDate;
        ValidateSchedule(startDate, durationMinutes, mustBeFuture: scheduleChanged);

        if (!ModelState.IsValid)
        {
            exam.Title = title ?? string.Empty;
            exam.StartDate = startDate;
            exam.DurationMinutes = durationMinutes;
            await PopulateSubjectsAndClassesAsync(exam.TeacherId, subjectId, classId);
            return View(exam);
        }

        exam.Title = title.Trim();
        exam.SubjectId = subjectId;
        exam.ClassId = classId;
        exam.StartDate = startDate;
        exam.DurationMinutes = durationMinutes;
        if (scheduleChanged) exam.ReminderSentAt = null;
        await _context.SaveChangesAsync();

        TempData["ExamSuccess"] = exam.Status == ExamStatus.Rejected
            ? "Exam updated. Submit it for approval again when you're ready."
            : "Exam updated.";
        return RedirectToAction(nameof(Questions), new { examId = exam.Id });
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
        if (!IsEditable(exam))
        {
            TempData["ExamError"] = "You can only add questions while the exam is a draft or has been rejected.";
            return RedirectToAction(nameof(Questions), new { examId });
        }

        ViewBag.Exam = exam;
        return View(new ExamQuestion { ExamId = examId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(ExamQuestion question, string? trueFalseAnswer, string? gapAnswers,
        string? markingGuide, IFormFile? attachment)
    {
        var exam = await GetOwnExamAsync(question.ExamId);
        if (exam == null) return NotFound();
        if (!IsEditable(exam)) return Forbid();

        ClearBindingErrors();
        ApplyAnswerKey(question, trueFalseAnswer, gapAnswers, markingGuide);
        var isUpload = question.QuestionType == QuestionTypes.Uploaded;
        if (isUpload) ValidateAttachment(attachment);
        ValidateQuestion(question, hasAttachment: attachment is { Length: > 0 });

        if (!ModelState.IsValid)
        {
            ViewBag.Exam = exam;
            ViewBag.TrueFalseAnswer = trueFalseAnswer;
            ViewBag.GapAnswers = gapAnswers;
            ViewBag.MarkingGuide = markingGuide;
            return View(question);
        }

        // Only uploaded questions carry a file; other types stand alone.
        question.AttachmentPath = isUpload ? await SaveAttachmentAsync(attachment!) : null;

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
        if (!IsEditable(exam))
        {
            TempData["ExamError"] = "You can only edit questions while the exam is a draft or has been rejected.";
            return RedirectToAction(nameof(Questions), new { examId = exam.Id });
        }

        ViewBag.Exam = exam;
        return View(question);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuestion(int id, ExamQuestion question, string? trueFalseAnswer, string? gapAnswers,
        string? markingGuide, IFormFile? attachment)
    {
        if (id != question.Id) return NotFound();

        // Load the stored question so its exam (and existing attachment) come from the database, not the form.
        var existing = await _context.ExamQuestions.FirstOrDefaultAsync(q => q.Id == id);
        if (existing == null) return NotFound();

        var exam = await GetOwnExamAsync(existing.ExamId);
        if (exam == null) return NotFound();
        if (!IsEditable(exam)) return Forbid();

        question.ExamId = existing.ExamId;
        question.AttachmentPath = existing.AttachmentPath;

        ClearBindingErrors();
        ApplyAnswerKey(question, trueFalseAnswer, gapAnswers, markingGuide);
        var isUpload = question.QuestionType == QuestionTypes.Uploaded;
        if (isUpload) ValidateAttachment(attachment);
        ValidateQuestion(question, hasAttachment: attachment is { Length: > 0 } || existing.AttachmentPath != null);

        if (!ModelState.IsValid)
        {
            ViewBag.Exam = exam;
            ViewBag.TrueFalseAnswer = trueFalseAnswer;
            ViewBag.GapAnswers = gapAnswers;
            ViewBag.MarkingGuide = markingGuide;
            return View(question);
        }

        // Only uploaded questions carry a file: a new upload replaces the old one, and
        // switching to another type drops it.
        var oldAttachment = existing.AttachmentPath;
        if (!isUpload)
        {
            existing.AttachmentPath = null;
        }
        else if (attachment is { Length: > 0 })
        {
            existing.AttachmentPath = await SaveAttachmentAsync(attachment);
        }

        existing.QuestionType = question.QuestionType;
        existing.QuestionText = question.QuestionText;
        existing.OptionA = question.OptionA;
        existing.OptionB = question.OptionB;
        existing.OptionC = question.OptionC;
        existing.OptionD = question.OptionD;
        existing.CorrectOption = question.CorrectOption;
        existing.Marks = question.Marks;

        await _context.SaveChangesAsync();

        if (oldAttachment != null && oldAttachment != existing.AttachmentPath)
        {
            DeleteAttachmentFile(oldAttachment);
        }

        TempData["ExamSuccess"] = "Question updated.";
        return RedirectToAction(nameof(Questions), new { examId = exam.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id, int examId)
    {
        var exam = await GetOwnExamAsync(examId);
        if (exam == null) return NotFound();
        if (!IsEditable(exam)) return Forbid();

        var question = await _context.ExamQuestions.FirstOrDefaultAsync(q => q.Id == id && q.ExamId == examId);
        if (question != null)
        {
            var attachmentPath = question.AttachmentPath;
            _context.ExamQuestions.Remove(question);
            await _context.SaveChangesAsync();
            if (attachmentPath != null) DeleteAttachmentFile(attachmentPath);
        }

        return RedirectToAction(nameof(Questions), new { examId });
    }

    // ---------- Question helpers ----------

    // With nullable reference types enabled, MVC treats every non-nullable string on
    // ExamQuestion as implicitly required. Which fields are actually needed depends on the
    // question type, so drop those automatic errors and let ValidateQuestion decide.
    // (Otherwise True/False and Fill-in-the-gap questions fail on the blank, hidden
    // Option A–D fields and are never saved.)
    private void ClearBindingErrors()
    {
        foreach (var key in new[]
                 {
                     nameof(ExamQuestion.Exam), nameof(ExamQuestion.QuestionText), nameof(ExamQuestion.QuestionType),
                     nameof(ExamQuestion.OptionA), nameof(ExamQuestion.OptionB), nameof(ExamQuestion.OptionC),
                     nameof(ExamQuestion.OptionD), nameof(ExamQuestion.CorrectOption)
                 })
        {
            ModelState.Remove(key);
        }
    }

    // Each question type posts its answer in its own field; store it in CorrectOption.
    private static void ApplyAnswerKey(ExamQuestion question, string? trueFalseAnswer, string? gapAnswers, string? markingGuide)
    {
        question.QuestionText = question.QuestionText?.Trim() ?? string.Empty;

        // Only uploaded (written) questions carry a teacher-chosen mark; the rest are worth 1.
        if (question.QuestionType != QuestionTypes.Uploaded) question.Marks = 1;

        switch (question.QuestionType)
        {
            case QuestionTypes.Uploaded:
                question.CorrectOption = markingGuide?.Trim() ?? string.Empty;
                question.OptionA = question.OptionB = question.OptionC = question.OptionD = string.Empty;
                break;
            case QuestionTypes.TrueFalse:
                question.CorrectOption = trueFalseAnswer ?? string.Empty;
                question.OptionA = question.OptionB = question.OptionC = question.OptionD = string.Empty;
                break;
            case QuestionTypes.FillInTheGap:
                question.CorrectOption = string.Join("\n", (gapAnswers ?? string.Empty)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                question.OptionA = question.OptionB = question.OptionC = question.OptionD = string.Empty;
                break;
            default:
                question.OptionA = question.OptionA?.Trim() ?? string.Empty;
                question.OptionB = question.OptionB?.Trim() ?? string.Empty;
                question.OptionC = question.OptionC?.Trim() ?? string.Empty;
                question.OptionD = question.OptionD?.Trim() ?? string.Empty;
                break;
        }
    }

    private void ValidateQuestion(ExamQuestion question, bool hasAttachment)
    {
        if (!QuestionTypes.All.Contains(question.QuestionType))
        {
            ModelState.AddModelError(nameof(ExamQuestion.QuestionType), "Choose a question type.");
            return;
        }

        // A blank or non-numeric marks box only matters for uploaded questions.
        ModelState.Remove(nameof(ExamQuestion.Marks));

        if (question.QuestionType == QuestionTypes.Uploaded)
        {
            if (!hasAttachment)
                ModelState.AddModelError("attachment", "Upload the question as an image or PDF.");
            if (question.Marks < 1 || question.Marks > ExamQuestion.MaxMarks
                || HttpContext.Request.Form.TryGetValue(nameof(ExamQuestion.Marks), out var posted) && !int.TryParse(posted, out _))
                ModelState.AddModelError(nameof(ExamQuestion.Marks), $"Enter how many marks this question is worth (1 to {ExamQuestion.MaxMarks}).");
        }
        else if (string.IsNullOrWhiteSpace(question.QuestionText))
        {
            ModelState.AddModelError(nameof(ExamQuestion.QuestionText), "Type the question.");
        }

        switch (question.QuestionType)
        {
            case QuestionTypes.MultipleChoice:
                if (string.IsNullOrWhiteSpace(question.OptionA)) ModelState.AddModelError(nameof(ExamQuestion.OptionA), "Option A is required.");
                if (string.IsNullOrWhiteSpace(question.OptionB)) ModelState.AddModelError(nameof(ExamQuestion.OptionB), "Option B is required.");
                if (string.IsNullOrWhiteSpace(question.OptionC)) ModelState.AddModelError(nameof(ExamQuestion.OptionC), "Option C is required.");
                if (string.IsNullOrWhiteSpace(question.OptionD)) ModelState.AddModelError(nameof(ExamQuestion.OptionD), "Option D is required.");
                if (!new[] { "A", "B", "C", "D" }.Contains(question.CorrectOption))
                    ModelState.AddModelError(nameof(ExamQuestion.CorrectOption), "Choose the correct option.");
                break;
            case QuestionTypes.TrueFalse:
                if (question.CorrectOption != "True" && question.CorrectOption != "False")
                    ModelState.AddModelError("trueFalseAnswer", "Choose whether the statement is True or False.");
                break;
            case QuestionTypes.FillInTheGap:
                if (string.IsNullOrWhiteSpace(question.CorrectOption))
                    ModelState.AddModelError("gapAnswers", "Enter at least one accepted answer.");
                break;
        }
    }

    private void ValidateAttachment(IFormFile? attachment)
    {
        if (attachment is not { Length: > 0 }) return;

        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
        if (!AllowedAttachmentExtensions.Contains(extension))
        {
            ModelState.AddModelError("attachment", "Upload an image (PNG, JPG, GIF, WebP) or a PDF.");
        }
        else if (attachment.Length > MaxAttachmentBytes)
        {
            ModelState.AddModelError("attachment", "The file must be 10 MB or smaller.");
        }
    }

    private async Task<string> SaveAttachmentAsync(IFormFile attachment)
    {
        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "exams");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(attachment.FileName).ToLowerInvariant()}";
        var filePath = Path.Combine(uploadsFolder, fileName);
        await using var stream = new FileStream(filePath, FileMode.Create);
        await attachment.CopyToAsync(stream);

        return $"/uploads/exams/{fileName}";
    }

    private void DeleteAttachmentFile(string attachmentPath)
    {
        // Only ever delete files inside uploads/exams.
        if (!attachmentPath.StartsWith("/uploads/exams/", StringComparison.Ordinal)) return;

        var fullPath = Path.Combine(_environment.WebRootPath, "uploads", "exams", Path.GetFileName(attachmentPath));
        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
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

        // Students can only sit a scheduled exam, so it needs a start time and duration
        // that haven't already passed.
        if (exam.StartDate == null || exam.DurationMinutes == null || exam.HasEnded(_clock.Now))
        {
            TempData["ExamError"] = exam.StartDate == null || exam.DurationMinutes == null
                ? "Set the exam's date, time and duration before sending it to students."
                : "This exam's scheduled time has already passed. Set a new date and time first.";
            return RedirectToAction(nameof(Notify), new { id });
        }

        exam.Status = ExamStatus.Published;
        await _context.SaveChangesAsync();

        TempData["ExamSuccess"] = $"Exam sent to students. It opens {ExamNotices.When(exam)}; students and parents get a reminder 24 hours before.";
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
            .Include(s => s.Answers)
            .Where(s => s.ExamId == id)
            .OrderByDescending(s => s.Score)
            .ToListAsync();

        ViewBag.Exam = exam;
        ViewBag.CanMark = !User.IsInRole("Admin") || await GetOwnExamAsync(id) != null;
        return View(submissions);
    }

    // ---------- Teacher: mark written answers to uploaded questions ----------

    public async Task<IActionResult> Mark(int id)
    {
        var submission = await LoadSubmissionForMarkingAsync(id);
        if (submission == null) return NotFound();

        return View(submission);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Mark(int id, [FromForm(Name = "marks")] Dictionary<int, string>? marks)
    {
        var submission = await LoadSubmissionForMarkingAsync(id);
        if (submission == null) return NotFound();

        // The teacher gives each written answer a score from 0 to the question's marks.
        // A blank box leaves that answer as it was.
        var written = submission.Answers.Where(a => a.ExamQuestion!.IsManuallyMarked).ToList();
        var scores = new Dictionary<int, int>();
        foreach (var answer in written)
        {
            if (marks == null || !marks.TryGetValue(answer.Id, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;

            var max = answer.ExamQuestion!.Marks;
            if (!int.TryParse(raw.Trim(), out var score) || score < 0 || score > max)
            {
                ModelState.AddModelError($"marks[{answer.Id}]", $"Enter a whole number from 0 to {max}.");
                continue;
            }
            scores[answer.Id] = score;
        }

        if (!ModelState.IsValid)
        {
            ViewBag.PostedMarks = marks;
            return View(submission);
        }

        foreach (var answer in written.Where(a => scores.ContainsKey(a.Id)))
        {
            answer.MarksAwarded = scores[answer.Id];
            answer.IsCorrect = answer.MarksAwarded == answer.ExamQuestion!.Marks;
        }

        submission.Score = submission.Answers.Sum(a => a.Points);
        submission.TotalMarks = submission.Answers.Sum(a => a.ExamQuestion!.Marks);
        await _context.SaveChangesAsync();

        TempData["ExamSuccess"] = submission.AwaitingMarking
            ? $"Marks saved for {submission.Student?.FullName}. Some answers still need marking."
            : $"Marking complete for {submission.Student?.FullName}: {submission.Score} / {submission.OutOf}.";
        return RedirectToAction(nameof(Results), new { id = submission.ExamId });
    }

    // Only the teacher who owns the exam can mark its submissions.
    private async Task<ExamSubmission?> LoadSubmissionForMarkingAsync(int submissionId)
    {
        var submission = await _context.ExamSubmissions
            .Include(s => s.Student)
            .Include(s => s.Exam).ThenInclude(e => e!.Subject)
            .Include(s => s.Answers).ThenInclude(a => a.ExamQuestion)
            .FirstOrDefaultAsync(s => s.Id == submissionId);
        if (submission == null) return null;

        var exam = await GetOwnExamAsync(submission.ExamId);
        return exam == null ? null : submission;
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
    public async Task<IActionResult> Notify(int id, DateTime? startDate, int? durationMinutes)
    {
        var exam = await GetOwnExamAsync(id);
        if (exam == null) return NotFound();
        if (exam.Status != ExamStatus.Approved && exam.Status != ExamStatus.Published)
        {
            return Forbid();
        }

        var scheduleChanged = startDate != exam.StartDate;
        ValidateSchedule(startDate, durationMinutes, mustBeFuture: scheduleChanged);
        if (!ModelState.IsValid)
        {
            exam.StartDate = startDate;
            exam.DurationMinutes = durationMinutes;
            return View(exam);
        }

        exam.StartDate = startDate;
        exam.DurationMinutes = durationMinutes;
        // A new start time gets its own 24-hour reminder.
        if (scheduleChanged) exam.ReminderSentAt = null;

        var senderName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Teacher";
        var subjectLine = $"Exam Notice: {exam.Title}";
        var body = ExamNotices.ScheduleText(exam, "is scheduled to start on");

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
        TempData["ExamSuccess"] = "Exam date and time sent to students and parents.";
        return RedirectToAction(nameof(Index));
    }

    // Exams need a start date/time (school-local) and a duration. New or changed start
    // times must be in the future.
    private void ValidateSchedule(DateTime? startDate, int? durationMinutes, bool mustBeFuture)
    {
        ModelState.Remove(nameof(Exam.StartDate));
        ModelState.Remove(nameof(Exam.DurationMinutes));

        if (startDate == null)
            ModelState.AddModelError(nameof(Exam.StartDate), "Set the date and time the exam starts.");
        else if (mustBeFuture && startDate.Value <= _clock.Now)
            ModelState.AddModelError(nameof(Exam.StartDate), "The start date and time must be in the future.");

        if (durationMinutes == null)
            ModelState.AddModelError(nameof(Exam.DurationMinutes), "Set how long the exam lasts.");
        else if (durationMinutes < 5 || durationMinutes > 600)
            ModelState.AddModelError(nameof(Exam.DurationMinutes), "Duration must be between 5 and 600 minutes.");
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
