using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

// One part (Assignment, CA or Exam) of a student's subject result, already scaled to the
// school's weight for that part (e.g. out of 20 for CA) and rounded.
public class ComponentScore
{
    // Worked out from graded assignments / marked CA tests / marked exams in the app.
    public decimal? Auto { get; set; }
    // How many graded assignments or marked tests make up Auto.
    public int Items { get; set; }
    // Typed on the score sheet or uploaded; replaces Auto.
    public decimal? Recorded { get; set; }
    // The recorded score before rounding (scaled to the current weight), shown in score
    // boxes and templates so saving or re-uploading never changes it.
    public decimal? RecordedExact { get; set; }

    public decimal? Value => Recorded ?? Auto;
    public bool IsRecorded => Recorded != null;
}

public class SubjectResult
{
    public int StudentId { get; set; }
    public int SubjectId { get; set; }
    public ComponentScore Assignment { get; } = new();
    public ComponentScore Ca { get; } = new();
    public ComponentScore Exam { get; } = new();

    public ComponentScore Get(string component) => component switch
    {
        ScoreComponents.Assignment => Assignment,
        ScoreComponents.Ca => Ca,
        _ => Exam
    };

    // The total is the sum of the rounded parts, so it always matches what is shown.
    public decimal? Total =>
        Assignment.Value == null && Ca.Value == null && Exam.Value == null
            ? null
            : (Assignment.Value ?? 0) + (Ca.Value ?? 0) + (Exam.Value ?? 0);

    public bool HasAny => Total != null;
}

// Works out students' subject results for a term from their assignments, CA tests and
// exams, plus any scores recorded on the score sheet or uploaded.
public class ResultsService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public ResultsService(ApplicationDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<GradingScheme> GetSchemeAsync() =>
        (await _context.Schools.AsNoTracking().FirstAsync(s => s.Id == _tenantProvider.SchoolId)).ToGradingScheme();

    // Every term that has work or scores, current term first.
    public async Task<List<string>> GetTermsAsync(GradingScheme scheme)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal) { scheme.CurrentTerm };
        terms.UnionWith(await _context.Assignments.Where(a => a.Term != null).Select(a => a.Term!).Distinct().ToListAsync());
        terms.UnionWith(await _context.Exams.Where(e => e.Term != null).Select(e => e.Term!).Distinct().ToListAsync());
        terms.UnionWith(await _context.SubjectScores.Select(s => s.Term).Distinct().ToListAsync());

        return terms.OrderBy(t => t == scheme.CurrentTerm ? 0 : 1).ThenByDescending(t => t).ToList();
    }

    public static string ResolveTerm(string? term, IReadOnlyCollection<string> terms, GradingScheme scheme) =>
        !string.IsNullOrWhiteSpace(term) && terms.Contains(term) ? term : scheme.CurrentTerm;

    // Results keyed by (student, subject). Only pairs with any work or score appear.
    public async Task<Dictionary<(int StudentId, int SubjectId), SubjectResult>> ComputeAsync(
        IReadOnlyCollection<int> studentIds, IReadOnlyCollection<int>? subjectIds, string term, GradingScheme scheme)
    {
        var results = new Dictionary<(int, int), SubjectResult>();
        if (studentIds.Count == 0) return results;

        SubjectResult For(int studentId, int subjectId)
        {
            if (!results.TryGetValue((studentId, subjectId), out var r))
            {
                r = new SubjectResult { StudentId = studentId, SubjectId = subjectId };
                results[(studentId, subjectId)] = r;
            }
            return r;
        }

        // Graded assignments: total score over total marks, scaled to the assignment weight.
        var assignmentQuery = _context.AssignmentSubmissions
            .Where(s => studentIds.Contains(s.StudentId) && s.Grade != null
                        && s.Assignment!.Term == term && s.Assignment.SubjectId != null);
        if (subjectIds != null)
            assignmentQuery = assignmentQuery.Where(s => subjectIds.Contains(s.Assignment!.SubjectId!.Value));
        var graded = await assignmentQuery
            .Select(s => new { s.StudentId, SubjectId = s.Assignment!.SubjectId!.Value, Score = s.Grade!.Value, Max = s.Assignment.MaxScore })
            .ToListAsync();
        foreach (var g in graded.GroupBy(g => (g.StudentId, g.SubjectId)))
        {
            var max = g.Sum(x => x.Max);
            var c = For(g.Key.StudentId, g.Key.SubjectId).Assignment;
            c.Items = g.Count();
            if (max > 0) c.Auto = scheme.Round(Math.Min(g.Sum(x => x.Score), max) / max * scheme.AssignmentWeight);
        }

        // Fully marked CA tests and exams taken in the app.
        var examQuery = _context.ExamSubmissions
            .Where(s => studentIds.Contains(s.StudentId) && s.Exam!.Term == term
                        && !s.Answers.Any(a => a.IsCorrect == null));
        if (subjectIds != null)
            examQuery = examQuery.Where(s => subjectIds.Contains(s.Exam!.SubjectId));
        var sat = await examQuery
            .Select(s => new { s.StudentId, s.Exam!.SubjectId, s.Exam.Kind, s.Score, OutOf = s.TotalMarks ?? s.TotalQuestions })
            .ToListAsync();
        foreach (var g in sat.GroupBy(x => (x.StudentId, x.SubjectId, x.Kind)))
        {
            var outOf = g.Sum(x => x.OutOf);
            var isCa = g.Key.Kind == ExamKinds.Ca;
            var r = For(g.Key.StudentId, g.Key.SubjectId);
            var c = isCa ? r.Ca : r.Exam;
            c.Items = g.Count();
            if (outOf > 0) c.Auto = scheme.Round((decimal)g.Sum(x => x.Score) / outOf * (isCa ? scheme.CaWeight : scheme.ExamWeight));
        }

        // Recorded scores replace the worked-out ones.
        var recordedQuery = _context.SubjectScores.Where(s => studentIds.Contains(s.StudentId) && s.Term == term);
        if (subjectIds != null) recordedQuery = recordedQuery.Where(s => subjectIds.Contains(s.SubjectId));
        foreach (var s in await recordedQuery.AsNoTracking().ToListAsync())
        {
            var r = For(s.StudentId, s.SubjectId);
            r.Assignment.RecordedExact = Exact(s.AssignmentScore, s.AssignmentOutOf, scheme.AssignmentWeight);
            r.Ca.RecordedExact = Exact(s.CaScore, s.CaOutOf, scheme.CaWeight);
            r.Exam.RecordedExact = Exact(s.ExamScore, s.ExamOutOf, scheme.ExamWeight);
            foreach (var c in new[] { r.Assignment, r.Ca, r.Exam })
                c.Recorded = c.RecordedExact == null ? null : scheme.Round(c.RecordedExact.Value);
        }

        // A part worth 0 marks doesn't count.
        foreach (var r in results.Values)
        {
            foreach (var component in ScoreComponents.All)
            {
                if (scheme.WeightOf(component) != 0) continue;
                var c = r.Get(component);
                c.Auto = c.Recorded = c.RecordedExact = null;
            }
        }

        return results;
    }

    // A recorded score scaled from the weight it was entered against to the current one.
    private static decimal? Exact(decimal? score, decimal? outOf, int weight)
    {
        if (score == null) return null;
        var from = outOf is > 0 ? outOf.Value : weight;
        return from == weight ? score.Value : Math.Round(score.Value / from * weight, 2, MidpointRounding.AwayFromZero);
    }

    // Subjects each student takes: their grade's subjects plus any given to them individually.
    public async Task<Dictionary<int, HashSet<int>>> SubjectsTakenAsync(IReadOnlyCollection<int> studentIds)
    {
        var students = await _context.Students.Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ClassId }).ToListAsync();
        var classIds = students.Where(s => s.ClassId != null).Select(s => s.ClassId!.Value).Distinct().ToList();
        var classSubjects = (await _context.SubjectClasses.Where(sc => classIds.Contains(sc.ClassId))
                .Select(sc => new { sc.ClassId, sc.SubjectId }).ToListAsync())
            .GroupBy(x => x.ClassId).ToDictionary(g => g.Key, g => g.Select(x => x.SubjectId).ToList());
        var individual = (await _context.StudentSubjects.Where(ss => studentIds.Contains(ss.StudentId))
                .Select(ss => new { ss.StudentId, ss.SubjectId }).ToListAsync())
            .GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.Select(x => x.SubjectId).ToList());

        return students.ToDictionary(s => s.Id, s =>
        {
            var set = new HashSet<int>();
            if (s.ClassId != null && classSubjects.TryGetValue(s.ClassId.Value, out var cs)) set.UnionWith(cs);
            if (individual.TryGetValue(s.Id, out var ind)) set.UnionWith(ind);
            return set;
        });
    }

    // Recorded score rows for these students and term, tracked so they can be changed and saved.
    public async Task<Dictionary<(int StudentId, int SubjectId), SubjectScore>> LoadRecordedAsync(
        IReadOnlyCollection<int> studentIds, string term) =>
        (await _context.SubjectScores.Where(s => studentIds.Contains(s.StudentId) && s.Term == term).ToListAsync())
            .ToDictionary(s => (s.StudentId, s.SubjectId));

    // Sets (or with null, clears) one recorded part, creating the row when needed.
    // The value is out of the part's current weight. Returns true if anything changed.
    public bool SetRecorded(Dictionary<(int StudentId, int SubjectId), SubjectScore> rows, int studentId, int subjectId,
        string term, string component, decimal? value, GradingScheme scheme, int userId)
    {
        rows.TryGetValue((studentId, subjectId), out var row);
        if (row == null)
        {
            if (value == null) return false;
            row = new SubjectScore { StudentId = studentId, SubjectId = subjectId, Term = term };
            _context.SubjectScores.Add(row);
            rows[(studentId, subjectId)] = row;
        }

        decimal? outOf = value == null ? null : scheme.WeightOf(component);
        bool changed;
        switch (component)
        {
            case ScoreComponents.Assignment:
                changed = row.AssignmentScore != value || row.AssignmentOutOf != outOf;
                row.AssignmentScore = value; row.AssignmentOutOf = outOf;
                break;
            case ScoreComponents.Ca:
                changed = row.CaScore != value || row.CaOutOf != outOf;
                row.CaScore = value; row.CaOutOf = outOf;
                break;
            default:
                changed = row.ExamScore != value || row.ExamOutOf != outOf;
                row.ExamScore = value; row.ExamOutOf = outOf;
                break;
        }

        if (changed)
        {
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = userId;
        }
        return changed;
    }

    // Removes rows left with no scores (call before saving).
    public void RemoveEmpty(Dictionary<(int StudentId, int SubjectId), SubjectScore> rows)
    {
        foreach (var row in rows.Values.Where(r => r.IsEmpty))
        {
            var entry = _context.Entry(row);
            if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
            else _context.SubjectScores.Remove(row);
        }
    }

    // Competition ranking: equal totals share a position (1, 2, 2, 4).
    public static Dictionary<T, int> Rank<T>(IEnumerable<(T Key, decimal? Total)> items) where T : notnull
    {
        var ranked = new Dictionary<T, int>();
        var list = items.Where(i => i.Total != null).OrderByDescending(i => i.Total).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            ranked[list[i].Key] = i > 0 && list[i].Total == list[i - 1].Total ? ranked[list[i - 1].Key] : i + 1;
        }
        return ranked;
    }

    // Everything recorded for one student in a term: subject results, assignments, CA tests, exams.
    public async Task<StudentRecordViewModel> BuildStudentRecordAsync(Student student, string? term)
    {
        var scheme = await GetSchemeAsync();
        var terms = await GetTermsAsync(scheme);
        var model = new StudentRecordViewModel
        {
            Student = student,
            Scheme = scheme,
            Terms = terms,
            Term = ResolveTerm(term, terms, scheme)
        };

        // Classmates are needed for the student's position in the grade.
        var classmateIds = student.ClassId == null
            ? new List<int> { student.Id }
            : await _context.Students.Where(s => s.ClassId == student.ClassId && (s.IsActive || s.Id == student.Id)).Select(s => s.Id).ToListAsync();
        var results = await ComputeAsync(classmateIds, null, model.Term, scheme);
        var taken = (await SubjectsTakenAsync(new[] { student.Id })).GetValueOrDefault(student.Id) ?? new HashSet<int>();

        var subjectIds = taken.Union(results.Keys.Where(k => k.StudentId == student.Id).Select(k => k.SubjectId)).ToList();
        var names = await _context.Subjects.Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
        model.Subjects = subjectIds.Where(names.ContainsKey).OrderBy(id => names[id]).Select(id => new StudentSubjectLine
        {
            SubjectId = id,
            SubjectName = names[id],
            Result = results.TryGetValue((student.Id, id), out var r) ? r : new SubjectResult { StudentId = student.Id, SubjectId = id }
        }).ToList();

        decimal? AverageOf(int studentId)
        {
            var totals = results.Values.Where(r => r.StudentId == studentId && r.Total != null).Select(r => r.Total!.Value).ToList();
            return totals.Any() ? scheme.Round(totals.Average()) : null;
        }
        model.Average = AverageOf(student.Id);
        var ranks = Rank(classmateIds.Select(id => (id, AverageOf(id))));
        model.Position = ranks.TryGetValue(student.Id, out var p) ? p : null;
        model.ClassSize = ranks.Count;

        model.Assignments = await _context.AssignmentSubmissions
            .Include(s => s.Assignment).ThenInclude(a => a!.Subject)
            .Where(s => s.StudentId == student.Id && s.Assignment!.Term == model.Term)
            .OrderBy(s => s.Assignment!.Subject!.Name).ThenBy(s => s.Assignment!.DueDate)
            .ToListAsync();

        var sat = await _context.ExamSubmissions
            .Include(s => s.Exam).ThenInclude(e => e!.Subject)
            .Include(s => s.Answers)
            .Where(s => s.StudentId == student.Id && s.Exam!.Term == model.Term)
            .OrderBy(s => s.Exam!.Subject!.Name).ThenBy(s => s.SubmittedAt)
            .ToListAsync();
        model.CaTests = sat.Where(s => s.Exam!.IsCaTest).ToList();
        model.Exams = sat.Where(s => !s.Exam!.IsCaTest).ToList();

        var attendance = await _context.AttendanceRecords.Where(a => a.StudentId == student.Id).Select(a => a.Status).ToListAsync();
        model.AttendanceDays = attendance.Count;
        model.PresentDays = attendance.Count(s => s == "Present");
        return model;
    }

    // Who a user can see: Admins everything; teachers their own grades and subjects.
    public async Task<ResultsScope> GetScopeAsync(int userId, bool isAdmin)
    {
        if (isAdmin)
        {
            return new ResultsScope
            {
                IsAdmin = true,
                ClassIds = await _context.Classes.Select(c => c.Id).ToListAsync(),
                SubjectIds = await _context.Subjects.Select(s => s.Id).ToListAsync()
            };
        }

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == userId);
        if (teacher == null) return new ResultsScope();

        return new ResultsScope
        {
            Teacher = teacher,
            ClassIds = await _context.TeacherClasses.Where(tc => tc.TeacherId == teacher.Id).Select(tc => tc.ClassId).ToListAsync(),
            SubjectIds = await _context.SubjectTeachers.Where(st => st.TeacherId == teacher.Id).Select(st => st.SubjectId).ToListAsync()
        };
    }
}

public class ResultsScope
{
    public bool IsAdmin { get; set; }
    public Teacher? Teacher { get; set; }
    public List<int> ClassIds { get; set; } = new();
    public List<int> SubjectIds { get; set; } = new();
}
