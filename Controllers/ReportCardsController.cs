using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

// Term report cards: a teacher prepares a grade's cards and submits them, the Admin
// approves (or sends them back), then the teacher sends them to students and parents.
[Authorize(Roles = "Admin,Teacher")]
public class ReportCardsController : Controller
{
    public const int MaxRemarkLength = 500;

    private readonly ApplicationDbContext _context;
    private readonly ResultsService _results;
    private readonly ReportCardService _cards;
    private readonly ITenantProvider _tenantProvider;

    public ReportCardsController(ApplicationDbContext context, ResultsService results, ReportCardService cards, ITenantProvider tenantProvider)
    {
        _context = context;
        _results = results;
        _cards = cards;
        _tenantProvider = tenantProvider;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");
    private string CurrentName => User.FindFirstValue("FullName") ?? (IsAdmin ? "School Admin" : "Teacher");
    private (int, string, string) Sender => (CurrentUserId, CurrentName, IsAdmin ? "Admin" : "Teacher");

    private async Task<(List<string> Terms, string Term)> TermAsync(string? term)
    {
        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        return (terms, ResultsService.ResolveTerm(term, terms, scheme));
    }

    // The grade, if the user may work on its report cards.
    private async Task<Class?> GradeAsync(int classId)
    {
        var scope = await _results.GetScopeAsync(CurrentUserId, IsAdmin);
        if (!scope.ClassIds.Contains(classId)) return null;
        return await _context.Classes.FirstOrDefaultAsync(c => c.Id == classId);
    }

    private Task<ReportCardBatch?> BatchAsync(int classId, string term) =>
        _context.ReportCardBatches.Include(b => b.Cards).FirstOrDefaultAsync(b => b.ClassId == classId && b.Term == term);

    private IActionResult BackToGrade(int classId, string term) => RedirectToAction(nameof(Grade), new { classId, term });

    public async Task<IActionResult> Index(string? term)
    {
        var scope = await _results.GetScopeAsync(CurrentUserId, IsAdmin);
        var (terms, resolved) = await TermAsync(term);
        var scheme = await _results.GetSchemeAsync();

        var classes = await _context.Classes.Where(c => scope.ClassIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync();
        var counts = await _context.Students.Where(s => s.IsActive && s.ClassId != null && scope.ClassIds.Contains(s.ClassId.Value))
            .GroupBy(s => s.ClassId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var batches = await _context.ReportCardBatches.Where(b => b.Term == resolved && scope.ClassIds.Contains(b.ClassId))
            .ToDictionaryAsync(b => b.ClassId);

        var grades = classes.Select(c => new ReportCardGradeLine
        {
            Class = c,
            Students = counts.GetValueOrDefault(c.Id),
            Batch = batches.GetValueOrDefault(c.Id)
        }).ToList();
        // The Admin sees what's waiting for approval first.
        if (scope.IsAdmin)
            grades = grades.OrderBy(g => g.Status == ReportCardStatus.Pending ? 0 : 1).ThenBy(g => g.Class.Name).ToList();

        return View(new ReportCardsIndexViewModel
        {
            IsAdmin = scope.IsAdmin,
            Term = resolved,
            Terms = terms,
            CurrentTerm = scheme.CurrentTerm,
            Grades = grades
        });
    }

    public async Task<IActionResult> Grade(int classId, string? term)
    {
        var cls = await GradeAsync(classId);
        if (cls == null) return NotFound();
        var (terms, resolved) = await TermAsync(term);
        var batch = await BatchAsync(classId, resolved);
        var live = await _cards.BuildAsync(classId, resolved);

        var model = new ReportCardGradeViewModel { IsAdmin = IsAdmin, Class = cls, Term = resolved, Terms = terms, Batch = batch };
        var cardsByStudent = batch?.Cards.ToDictionary(c => c.StudentId) ?? new Dictionary<int, ReportCard>();

        List<(int StudentId, ReportCardData Data, string? Remark)> rows;
        if (model.IsEditable)
        {
            rows = live.Select(x => (x.Key, x.Value, cardsByStudent.GetValueOrDefault(x.Key)?.TeacherRemark)).ToList();
        }
        else
        {
            // Show exactly what was submitted / approved.
            rows = cardsByStudent.Values.Where(c => c.DataJson != null)
                .Select(c => (c.StudentId, c.Data!, c.Data!.TeacherRemark))
                .OrderBy(r => r.Item2.StudentName).ToList();
            var frozenIds = rows.Select(r => r.StudentId).ToHashSet();
            model.ScoresChangedSinceSubmit = !frozenIds.SetEquals(live.Keys)
                || rows.Any(r => live.TryGetValue(r.StudentId, out var now) && now.ScoresKey() != r.Data.ScoresKey());
        }

        var ids = rows.Select(r => r.StudentId).ToList();
        var logins = await _context.Students.Where(s => ids.Contains(s.Id) && s.UserId != null).Select(s => s.Id).ToListAsync();
        var parents = await _context.ParentStudents.Where(p => ids.Contains(p.StudentId))
            .GroupBy(p => p.StudentId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        model.Rows = rows.Select(r => new ReportCardGradeRow
        {
            StudentId = r.StudentId,
            Data = r.Data,
            Remark = r.Remark,
            HasLogin = logins.Contains(r.StudentId),
            Parents = parents.GetValueOrDefault(r.StudentId),
            SentAt = cardsByStudent.GetValueOrDefault(r.StudentId)?.SentAt
        }).ToList();
        model.StudentsWithoutAccounts = model.Rows.Count(r => !r.HasLogin && r.Parents == 0);
        return View(model);
    }

    // Saves the teacher's remarks and, with submit, sends the cards to the Admin for approval.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int classId, string? term, [FromForm(Name = "remarks")] Dictionary<int, string?>? remarks, string? note, bool submit)
    {
        var cls = await GradeAsync(classId);
        if (cls == null) return NotFound();
        var (_, resolved) = await TermAsync(term);
        var batch = await BatchAsync(classId, resolved);
        if (batch != null && !batch.IsEditable)
        {
            TempData["ReportError"] = "These report cards have already been submitted. Withdraw or revise them first.";
            return BackToGrade(classId, resolved);
        }
        remarks ??= new Dictionary<int, string?>();
        if (remarks.Values.Any(r => (r?.Trim().Length ?? 0) > MaxRemarkLength))
        {
            TempData["ReportError"] = $"Remarks can be at most {MaxRemarkLength} characters. Nothing was saved.";
            return BackToGrade(classId, resolved);
        }

        var live = await _cards.BuildAsync(classId, resolved);
        if (submit && live.Count == 0)
        {
            TempData["ReportError"] = $"There are no active students in {cls.Name}.";
            return BackToGrade(classId, resolved);
        }

        if (batch == null)
        {
            batch = new ReportCardBatch { SchoolId = cls.SchoolId, ClassId = classId, Term = resolved, Status = ReportCardStatus.Draft };
            _context.ReportCardBatches.Add(batch);
        }
        var cards = batch.Cards.ToDictionary(c => c.StudentId);
        foreach (var (studentId, data) in live)
        {
            if (!cards.TryGetValue(studentId, out var card))
            {
                card = new ReportCard { SchoolId = cls.SchoolId, StudentId = studentId };
                batch.Cards.Add(card);
                cards[studentId] = card;
            }
            if (remarks.TryGetValue(studentId, out var remark))
                card.TeacherRemark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
            if (submit)
            {
                data.TeacherRemark = card.TeacherRemark;
                data.PreparedBy = CurrentName;
                card.DataJson = data.ToJson();
            }
        }

        if (!submit)
        {
            await _context.SaveChangesAsync();
            TempData["ReportSuccess"] = "Remarks saved.";
            return BackToGrade(classId, resolved);
        }

        // Students no longer in the grade aren't part of this submission.
        foreach (var card in batch.Cards.Where(c => !live.ContainsKey(c.StudentId))) card.DataJson = null;
        batch.Status = ReportCardStatus.Pending;
        batch.SubmittedByUserId = CurrentUserId;
        batch.SubmittedByName = CurrentName;
        batch.SubmittedAt = DateTime.UtcNow;
        batch.SubmitNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        batch.ReviewedByUserId = null;
        batch.ReviewedByName = null;
        batch.ReviewedAt = null;
        batch.ReviewNote = null;

        _cards.Notify(Sender, MessageAudience.Admin, classId, $"Report cards to approve: {cls.Name}, {resolved}",
            $"{CurrentName} submitted the {resolved} report cards for {cls.Name} ({live.Count} students) for your approval." +
            (batch.SubmitNote != null ? $"\n\nNote: {batch.SubmitNote}" : "") +
            "\n\nOpen Report Approval to review them.", await _cards.AdminUserIdsAsync());
        await _context.SaveChangesAsync();
        TempData["ReportSuccess"] = $"Submitted {live.Count} report cards to the Admin for approval.";
        return BackToGrade(classId, resolved);
    }

    // Takes back a submission that hasn't been reviewed yet.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int classId, string? term)
    {
        if (await GradeAsync(classId) == null) return NotFound();
        var (_, resolved) = await TermAsync(term);
        var batch = await BatchAsync(classId, resolved);
        if (batch?.Status != ReportCardStatus.Pending) return BackToGrade(classId, resolved);
        batch.Status = ReportCardStatus.Draft;
        await _context.SaveChangesAsync();
        TempData["ReportSuccess"] = "Submission withdrawn. You can change the remarks and submit again.";
        return BackToGrade(classId, resolved);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Review(int classId, string? term, string? decision, string? note)
    {
        var cls = await GradeAsync(classId);
        if (cls == null) return NotFound();
        var (_, resolved) = await TermAsync(term);
        var batch = await BatchAsync(classId, resolved);
        if (batch?.Status != ReportCardStatus.Pending)
        {
            TempData["ReportError"] = "These report cards are not waiting for approval.";
            return BackToGrade(classId, resolved);
        }
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var approve = decision == "approve";
        if (!approve && note == null)
        {
            TempData["ReportError"] = "Say what needs to change so the teacher can fix it.";
            return BackToGrade(classId, resolved);
        }

        var now = DateTime.UtcNow;
        batch.Status = approve ? ReportCardStatus.Approved : ReportCardStatus.Rejected;
        batch.ReviewedByUserId = CurrentUserId;
        batch.ReviewedByName = CurrentName;
        batch.ReviewedAt = now;
        batch.ReviewNote = note;
        if (approve)
        {
            foreach (var card in batch.Cards.Where(c => c.DataJson != null))
            {
                var data = card.Data!;
                data.ApprovedBy = CurrentName;
                data.ApprovedAt = now;
                card.DataJson = data.ToJson();
            }
        }

        if (batch.SubmittedByUserId != null)
        {
            _cards.Notify(Sender, MessageAudience.Teacher, classId,
                approve ? $"Report cards approved: {cls.Name}, {resolved}" : $"Report cards sent back: {cls.Name}, {resolved}",
                approve
                    ? $"The {resolved} report cards for {cls.Name} were approved. Open Report Cards and click \"Send to students & parents\"." + (note != null ? $"\n\nNote: {note}" : "")
                    : $"The {resolved} report cards for {cls.Name} need changes before they can be approved:\n\n{note}\n\nOpen Report Cards, make the changes and submit them again.",
                new[] { batch.SubmittedByUserId.Value });
        }
        await _context.SaveChangesAsync();
        TempData["ReportSuccess"] = approve
            ? $"Approved. {batch.SubmittedByName ?? "The teacher"} can now send the report cards to students and parents."
            : "Sent back to the teacher with your note.";
        return BackToGrade(classId, resolved);
    }

    // One click: every approved card goes to the student and their parents.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int classId, string? term)
    {
        var cls = await GradeAsync(classId);
        if (cls == null) return NotFound();
        var (_, resolved) = await TermAsync(term);
        var batch = await BatchAsync(classId, resolved);
        if (batch?.Status != ReportCardStatus.Approved)
        {
            TempData["ReportError"] = "Report cards can only be sent after the Admin approves them.";
            return BackToGrade(classId, resolved);
        }

        var cards = batch.Cards.Where(c => c.DataJson != null).ToList();
        var ids = cards.Select(c => c.StudentId).ToList();
        var studentUsers = await _context.Students.Where(s => ids.Contains(s.Id) && s.UserId != null)
            .ToDictionaryAsync(s => s.Id, s => s.UserId!.Value);
        var parentUsers = (await _context.ParentStudents.Where(p => ids.Contains(p.StudentId)).ToListAsync())
            .GroupBy(p => p.StudentId).ToDictionary(g => g.Key, g => g.Select(p => p.ParentUserId).Distinct().ToList());

        var now = DateTime.UtcNow;
        var resend = batch.TimesSent > 0;
        int studentsReached = 0, parentsReached = 0, unreachable = 0;
        foreach (var card in cards)
        {
            var data = card.Data!;
            data.IssuedAt = now;
            card.SentDataJson = data.ToJson();
            card.SentAt = now;

            var summary = $"Average: {data.Format(data.Average)} out of {data.Total}" +
                          (data.Position != null ? $", position {data.Position} of {data.ClassSize}" : "") + ".";
            var title = $"{(resend ? "Updated report card" : "Report card")}: {data.StudentName}, {resolved}";
            var hasStudent = studentUsers.TryGetValue(card.StudentId, out var studentUserId);
            var parents = parentUsers.GetValueOrDefault(card.StudentId) ?? new List<int>();
            if (hasStudent)
            {
                _cards.Notify(Sender, MessageAudience.Student, classId, title,
                    $"Your {resolved} report card ({cls.Name}) is ready. {summary}\n\nOpen Report Cards in your portal to see and print it.",
                    new[] { studentUserId });
                studentsReached++;
            }
            if (parents.Any())
            {
                _cards.Notify(Sender, MessageAudience.Parent, classId, title,
                    $"{data.StudentName}'s {resolved} report card ({cls.Name}) is ready. {summary}\n\nOpen My Children, choose {data.StudentName} and see Report cards to view and print it.",
                    parents);
                parentsReached += parents.Count;
            }
            if (!hasStudent && !parents.Any()) unreachable++;
        }

        batch.Status = ReportCardStatus.Sent;
        batch.SentByUserId = CurrentUserId;
        batch.SentByName = CurrentName;
        batch.SentAt = now;
        batch.TimesSent++;
        await _context.SaveChangesAsync();

        TempData["ReportSuccess"] = $"Sent {cards.Count} report cards: {studentsReached} student(s) and {parentsReached} parent account(s) were notified." +
            (unreachable > 0 ? $" {unreachable} student(s) have no login or linked parent; they'll see their card once an account is set up, or you can print it." : "");
        return BackToGrade(classId, resolved);
    }

    // Reopens approved or sent cards for changes; they need approval again before re-sending.
    // Students and parents keep seeing the last card sent to them meanwhile.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revise(int classId, string? term)
    {
        if (await GradeAsync(classId) == null) return NotFound();
        var (_, resolved) = await TermAsync(term);
        var batch = await BatchAsync(classId, resolved);
        if (batch?.Status is not (ReportCardStatus.Approved or ReportCardStatus.Sent)) return BackToGrade(classId, resolved);
        batch.Status = ReportCardStatus.Draft;
        await _context.SaveChangesAsync();
        TempData["ReportSuccess"] = "Reopened for changes. Submit them again for approval when ready." +
            (batch.TimesSent > 0 ? " Students and parents keep seeing the card already sent until the new one is approved and sent." : "");
        return BackToGrade(classId, resolved);
    }

    public async Task<IActionResult> Card(int classId, int studentId, string? term)
    {
        var cls = await GradeAsync(classId);
        if (cls == null) return NotFound();
        var (_, resolved) = await TermAsync(term);
        var batch = await BatchAsync(classId, resolved);
        var card = batch?.Cards.FirstOrDefault(c => c.StudentId == studentId);

        var model = new ReportCardPageViewModel
        {
            BackText = $"Back to {cls.Name} report cards",
            BackUrl = Url.Action(nameof(Grade), new { classId, term = resolved })
        };
        if (batch != null && !batch.IsEditable && card?.DataJson != null)
        {
            model.Data = card.Data!;
            model.Notice = batch.Status switch
            {
                ReportCardStatus.Pending => "Waiting for the Admin's approval.",
                ReportCardStatus.Approved => "Approved, not sent to the student and parents yet.",
                _ => null
            };
        }
        else
        {
            var live = await _cards.BuildAsync(classId, resolved);
            if (!live.TryGetValue(studentId, out var data)) return NotFound();
            data.TeacherRemark = card?.TeacherRemark;
            model.Data = data;
            model.Notice = "Preview: scores can still change. Students and parents get the card only after the Admin approves it and it is sent.";
        }
        return View("ReportCardPage", model);
    }
}
