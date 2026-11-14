using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

// Builds report cards for a grade and sends notices about them.
public class ReportCardService
{
    private readonly ApplicationDbContext _context;
    private readonly ResultsService _results;
    private readonly ITenantProvider _tenantProvider;

    public ReportCardService(ApplicationDbContext context, ResultsService results, ITenantProvider tenantProvider)
    {
        _context = context;
        _results = results;
        _tenantProvider = tenantProvider;
    }

    // Report cards for every active student in the grade, worked out from the current scores.
    public async Task<Dictionary<int, ReportCardData>> BuildAsync(int classId, string term)
    {
        var scheme = await _results.GetSchemeAsync();
        var className = await _context.Classes.Where(c => c.Id == classId).Select(c => c.Name).FirstOrDefaultAsync() ?? string.Empty;
        var students = await _context.Students.Where(s => s.ClassId == classId && s.IsActive)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync();
        var ids = students.Select(s => s.Id).ToList();

        var results = await _results.ComputeAsync(ids, null, term, scheme);
        var taken = await _results.SubjectsTakenAsync(ids);
        var subjectIds = taken.Values.SelectMany(x => x).Union(results.Keys.Select(k => k.SubjectId)).Distinct().ToList();
        var names = await _context.Subjects.Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name.Trim());

        decimal? AverageOf(int studentId)
        {
            var totals = results.Values.Where(r => r.StudentId == studentId && r.Total != null).Select(r => r.Total!.Value).ToList();
            return totals.Any() ? scheme.Round(totals.Average()) : null;
        }
        var averages = ids.ToDictionary(id => id, AverageOf);
        var ranks = ResultsService.Rank(ids.Select(id => (id, averages[id])));

        var attendance = (await _context.AttendanceRecords.Where(a => ids.Contains(a.StudentId))
                .Select(a => new { a.StudentId, a.Status }).ToListAsync())
            .GroupBy(a => a.StudentId)
            .ToDictionary(g => g.Key, g => (Days: g.Count(), Present: g.Count(a => a.Status == "Present")));

        var cards = new Dictionary<int, ReportCardData>();
        foreach (var student in students)
        {
            var mine = (taken.GetValueOrDefault(student.Id) ?? new HashSet<int>())
                .Union(results.Keys.Where(k => k.StudentId == student.Id).Select(k => k.SubjectId))
                .Where(names.ContainsKey)
                .OrderBy(id => names[id]);
            var att = attendance.GetValueOrDefault(student.Id);
            cards[student.Id] = new ReportCardData
            {
                SchoolName = _tenantProvider.SchoolName ?? string.Empty,
                StudentId = student.Id,
                StudentName = student.FullName,
                AdmissionNumber = student.AdmissionNumber,
                ClassName = className,
                Term = term,
                AssignmentWeight = scheme.AssignmentWeight,
                CaWeight = scheme.CaWeight,
                ExamWeight = scheme.ExamWeight,
                Decimals = scheme.Decimals,
                Subjects = mine.Select(id =>
                {
                    results.TryGetValue((student.Id, id), out var r);
                    return new ReportCardSubject
                    {
                        Name = names[id],
                        Assignment = r?.Assignment.Value,
                        Ca = r?.Ca.Value,
                        Exam = r?.Exam.Value,
                        Total = r?.Total
                    };
                }).ToList(),
                Average = averages[student.Id],
                Position = ranks.TryGetValue(student.Id, out var p) ? p : null,
                ClassSize = ranks.Count,
                AttendanceDays = att.Days,
                PresentDays = att.Present
            };
        }
        return cards;
    }

    public async Task<List<int>> AdminUserIdsAsync()
    {
        var schoolId = _tenantProvider.SchoolId;
        return await _context.UserRoles
            .Where(ur => ur.Role.Name == "Admin" && ur.User.SchoolId == schoolId && ur.User.IsActive)
            .Select(ur => ur.UserId).Distinct().ToListAsync();
    }

    // Adds (doesn't save) a direct in-app message.
    public void Notify((int UserId, string Name, string Role) sender, string audience, int? classId,
        string subject, string body, IEnumerable<int> recipientUserIds)
    {
        var recipients = recipientUserIds.Distinct().Where(id => id != sender.UserId).ToList();
        if (!recipients.Any()) return;
        var schoolId = _tenantProvider.SchoolId;
        _context.Messages.Add(new Message
        {
            SchoolId = schoolId,
            SenderUserId = sender.UserId,
            SenderName = sender.Name,
            SenderRole = sender.Role,
            Audience = audience,
            Scope = MessageScope.Individual,
            ClassId = classId,
            Subject = subject,
            Body = body,
            Recipients = recipients.Select(id => new MessageRecipient { SchoolId = schoolId, RecipientUserId = id }).ToList()
        });
    }
}
