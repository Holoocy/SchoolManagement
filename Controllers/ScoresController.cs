using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

// Score sheet: each student's Assignment, CA and Exam scores per subject and term.
// Teachers work with their own grades and subjects; Admins with everything.
[Authorize(Roles = "Admin,Teacher")]
public class ScoresController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ResultsService _results;
    private readonly ScoreSheetFiles _files;
    private readonly ITenantProvider _tenantProvider;

    public ScoresController(ApplicationDbContext context, ResultsService results, ScoreSheetFiles files, ITenantProvider tenantProvider)
    {
        _context = context;
        _results = results;
        _files = files;
        _tenantProvider = tenantProvider;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    private Task<ResultsScope> ScopeAsync() => _results.GetScopeAsync(CurrentUserId, IsAdmin);

    private async Task<(List<Class> Classes, List<Subject> Subjects)> OptionsAsync(ResultsScope scope) =>
        (await _context.Classes.Where(c => scope.ClassIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(),
         await _context.Subjects.Where(s => scope.SubjectIds.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync());

    // ---------- score sheet ----------

    public async Task<IActionResult> Index(int? classId, int? subjectId, string? term)
    {
        var model = await BuildSheetAsync(classId, subjectId, term);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(int classId, int subjectId, string term, List<ScoreSheetInput>? rows)
    {
        var scope = await ScopeAsync();
        if (!scope.ClassIds.Contains(classId) || !scope.SubjectIds.Contains(subjectId)) return NotFound();

        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        if (!terms.Contains(term)) return BadRequest();

        rows ??= new List<ScoreSheetInput>();
        var studentIds = await _context.Students.Where(s => s.ClassId == classId && s.IsActive).Select(s => s.Id).ToListAsync();
        var posted = rows.Where(r => studentIds.Contains(r.StudentId)).GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.First());

        // Check everything first; nothing is saved if any score is wrong.
        var parsed = new Dictionary<(int, string), decimal?>();
        var errors = new Dictionary<(int, string), string>();
        foreach (var (studentId, input) in posted)
        {
            foreach (var component in ScoreComponents.All)
            {
                var weight = scheme.WeightOf(component);
                if (weight == 0) continue;
                var raw = input.Get(component)?.Trim();
                if (string.IsNullOrEmpty(raw)) { parsed[(studentId, component)] = null; continue; }
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0 || value > weight)
                {
                    errors[(studentId, component)] = $"0 to {weight}";
                    continue;
                }
                parsed[(studentId, component)] = Math.Round(value, 2);
            }
        }

        if (errors.Any())
        {
            var model = await BuildSheetAsync(classId, subjectId, term);
            model.HasErrors = true;
            foreach (var row in model.Rows)
            {
                if (!posted.TryGetValue(row.StudentId, out var input)) continue;
                foreach (var component in ScoreComponents.All) row.Posted[component] = input.Get(component);
                foreach (var component in ScoreComponents.All)
                    if (errors.TryGetValue((row.StudentId, component), out var message)) row.Errors[component] = message;
            }
            return View(model);
        }

        var recorded = await _results.LoadRecordedAsync(posted.Keys.ToList(), term);
        var changedStudents = new HashSet<int>();
        foreach (var ((studentId, component), value) in parsed)
        {
            if (_results.SetRecorded(recorded, studentId, subjectId, term, component, value, scheme, CurrentUserId))
                changedStudents.Add(studentId);
        }
        _results.RemoveEmpty(recorded);
        await _context.SaveChangesAsync();

        TempData["ScoresSuccess"] = changedStudents.Count == 0
            ? "No scores changed."
            : $"Scores saved for {changedStudents.Count} student(s).";
        return RedirectToAction(nameof(Index), new { classId, subjectId, term });
    }

    private async Task<ScoreSheetViewModel> BuildSheetAsync(int? classId, int? subjectId, string? term)
    {
        var scope = await ScopeAsync();
        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        var (classes, subjects) = await OptionsAsync(scope);

        // Pre-select when there is only one choice.
        if (classId == null && classes.Count == 1) classId = classes[0].Id;
        if (subjectId == null && subjects.Count == 1) subjectId = subjects[0].Id;
        if (classId != null && !scope.ClassIds.Contains(classId.Value)) classId = null;
        if (subjectId != null && !scope.SubjectIds.Contains(subjectId.Value)) subjectId = null;

        var model = new ScoreSheetViewModel
        {
            Scheme = scheme,
            Terms = terms,
            Term = ResultsService.ResolveTerm(term, terms, scheme),
            Classes = classes,
            Subjects = subjects,
            ClassId = classId,
            SubjectId = subjectId,
            IsAdmin = scope.IsAdmin,
            ClassName = classes.FirstOrDefault(c => c.Id == classId)?.Name,
            SubjectName = subjects.FirstOrDefault(s => s.Id == subjectId)?.Name
        };
        if (!model.Ready) return model;

        var students = await _context.Students.Where(s => s.ClassId == classId && s.IsActive)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync();
        var results = await _results.ComputeAsync(students.Select(s => s.Id).ToList(), new[] { subjectId!.Value }, model.Term, scheme);
        var ranks = ResultsService.Rank(students.Select(s => (s.Id, results.TryGetValue((s.Id, subjectId.Value), out var r) ? r.Total : null)));

        model.Rows = students.Select(s => new ScoreSheetRow
        {
            StudentId = s.Id,
            Name = s.FullName,
            AdmissionNumber = s.AdmissionNumber,
            Result = results.TryGetValue((s.Id, subjectId.Value), out var r) ? r : new SubjectResult { StudentId = s.Id, SubjectId = subjectId.Value },
            Position = ranks.TryGetValue(s.Id, out var p) ? p : null
        }).ToList();
        return model;
    }

    // ---------- upload ----------

    public async Task<IActionResult> Upload(int? classId, int? subjectId, string? term) =>
        View(await BuildUploadAsync(classId, subjectId, term));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ScoreSheetFiles.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, string? term, int? subjectId)
    {
        var scope = await ScopeAsync();
        var model = await BuildUploadAsync(null, subjectId, term);
        if (subjectId != null && !scope.SubjectIds.Contains(subjectId.Value)) subjectId = null;

        model.Result = await _files.ImportAsync(file, model.Term, subjectId, scope, model.Scheme, CurrentUserId);
        return View(model);
    }

    private async Task<ScoreUploadViewModel> BuildUploadAsync(int? classId, int? subjectId, string? term)
    {
        var scope = await ScopeAsync();
        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        var (classes, subjects) = await OptionsAsync(scope);
        return new ScoreUploadViewModel
        {
            Scheme = scheme,
            Terms = terms,
            Term = ResultsService.ResolveTerm(term, terms, scheme),
            Classes = classes,
            Subjects = subjects,
            ClassId = classId != null && scope.ClassIds.Contains(classId.Value) ? classId : null,
            SubjectId = subjectId != null && scope.SubjectIds.Contains(subjectId.Value) ? subjectId : null,
            IsAdmin = scope.IsAdmin
        };
    }

    // Template with every student (by grade or all) and their subjects (one or all),
    // with any scores already recorded for the term filled in.
    public async Task<IActionResult> Template(string? format, int? classId, int? subjectId, string? term)
    {
        var scope = await ScopeAsync();
        var scheme = await _results.GetSchemeAsync();
        var terms = await _results.GetTermsAsync(scheme);
        term = ResultsService.ResolveTerm(term, terms, scheme);
        if (classId != null && !scope.ClassIds.Contains(classId.Value)) return NotFound();
        if (subjectId != null && !scope.SubjectIds.Contains(subjectId.Value)) return NotFound();

        var studentQuery = _context.Students.Include(s => s.Class).Where(s => s.IsActive);
        studentQuery = classId != null
            ? studentQuery.Where(s => s.ClassId == classId)
            : scope.IsAdmin ? studentQuery : studentQuery.Where(s => s.ClassId != null && scope.ClassIds.Contains(s.ClassId.Value));
        var students = await studentQuery.OrderBy(s => s.Class!.Name).ThenBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync();

        var subjects = await _context.Subjects.Where(s => scope.SubjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var studentIds = students.Select(s => s.Id).ToList();
        var taken = await _results.SubjectsTakenAsync(studentIds);
        var recorded = await _results.ComputeAsync(studentIds,
            subjectId != null ? new List<int> { subjectId.Value } : scope.SubjectIds, term, scheme);

        var rows = new List<ScoreTemplateRow>();
        foreach (var student in students)
        {
            IEnumerable<int> subjectIds;
            if (subjectId != null) subjectIds = new[] { subjectId.Value };
            else
            {
                var mine = taken.TryGetValue(student.Id, out var t) ? t.Where(subjects.ContainsKey).ToList() : new List<int>();
                // A teacher's subjects may not be linked to their grades yet; list them all.
                subjectIds = mine.Count == 0 && !scope.IsAdmin ? subjects.Keys : mine;
            }

            foreach (var sid in subjectIds.Where(subjects.ContainsKey).OrderBy(id => subjects[id].Name))
            {
                recorded.TryGetValue((student.Id, sid), out var r);
                rows.Add(new ScoreTemplateRow
                {
                    StudentId = student.AdmissionNumber,
                    StudentName = student.FullName,
                    Grade = student.Class?.Name ?? string.Empty,
                    Subject = subjects[sid].Name,
                    Assignment = r?.Assignment.RecordedExact,
                    Ca = r?.Ca.RecordedExact,
                    Exam = r?.Exam.RecordedExact
                });
            }
        }

        var gradeName = classId != null ? students.FirstOrDefault()?.Class?.Name
            ?? await _context.Classes.Where(c => c.Id == classId).Select(c => c.Name).FirstOrDefaultAsync() : "all-grades";
        var subjectName = subjectId != null ? subjects[subjectId.Value].Name : "all-subjects";
        var fileName = Slug($"scores {gradeName} {subjectName} {term}");

        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(ScoreSheetFiles.TemplateCsv(rows, scheme), "text/csv", fileName + ".csv")
            : File(ScoreSheetFiles.TemplateXlsx(rows, scheme, term), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName + ".xlsx");
    }

    private static string Slug(string value) =>
        Regex.Replace(Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-"), "-+", "-").Trim('-');

    // ---------- grading setup (Admin) ----------

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Setup()
    {
        var scheme = await _results.GetSchemeAsync();
        return View(new GradingSetupViewModel
        {
            AssignmentWeight = scheme.AssignmentWeight,
            CaWeight = scheme.CaWeight,
            ExamWeight = scheme.ExamWeight,
            ScoreDecimals = scheme.Decimals,
            CurrentTerm = scheme.CurrentTerm,
            Terms = await _results.GetTermsAsync(scheme)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Setup(GradingSetupViewModel model)
    {
        model.CurrentTerm = Regex.Replace(model.CurrentTerm ?? string.Empty, @"\s+", " ").Trim();
        foreach (var (name, value) in new[] { (nameof(model.AssignmentWeight), model.AssignmentWeight), (nameof(model.CaWeight), model.CaWeight), (nameof(model.ExamWeight), model.ExamWeight) })
        {
            if (value < 0 || value > 100) ModelState.AddModelError(name, "Enter a number from 0 to 100.");
        }
        if (ModelState.IsValid && model.AssignmentWeight + model.CaWeight + model.ExamWeight != 100)
            ModelState.AddModelError(string.Empty, $"Assignment, CA and Exam must add up to 100 (they add up to {model.AssignmentWeight + model.CaWeight + model.ExamWeight}).");
        if (model.ScoreDecimals is < 0 or > 2) ModelState.AddModelError(nameof(model.ScoreDecimals), "Choose how to round scores.");
        if (model.CurrentTerm.Length == 0) ModelState.AddModelError(nameof(model.CurrentTerm), "Enter the current term, e.g. First Term 2026/2027.");
        else if (model.CurrentTerm.Length > 60) ModelState.AddModelError(nameof(model.CurrentTerm), "Keep the term name under 60 characters.");

        if (!ModelState.IsValid)
        {
            model.Terms = await _results.GetTermsAsync(await _results.GetSchemeAsync());
            return View(model);
        }

        var school = await _context.Schools.FirstAsync(s => s.Id == _tenantProvider.SchoolId);
        school.AssignmentWeight = model.AssignmentWeight;
        school.CaWeight = model.CaWeight;
        school.ExamWeight = model.ExamWeight;
        school.ScoreDecimals = model.ScoreDecimals;
        school.CurrentTerm = model.CurrentTerm;
        await _context.SaveChangesAsync();

        TempData["ScoresSuccess"] = $"Grading saved: Assignment {model.AssignmentWeight}, CA {model.CaWeight}, Exam {model.ExamWeight}. Current term: {model.CurrentTerm}.";
        return RedirectToAction(nameof(Setup));
    }
}
