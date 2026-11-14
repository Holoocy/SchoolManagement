using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

// Sends students and parents an automatic reminder 24 hours before a published exam
// starts. Runs every minute across all schools; each exam is reminded once (or again
// if its start time is changed). An exam published less than 24 hours before it starts
// gets its reminder straight away.
public class ExamReminderService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISchoolClock _clock;
    private readonly ILogger<ExamReminderService> _logger;

    public ExamReminderService(IServiceScopeFactory scopeFactory, ISchoolClock clock, ILogger<ExamReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SendDueRemindersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Sending exam reminders failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDueRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // No school is resolved outside a request, so tenant filters are ignored and
        // every record below is scoped to the exam's own school explicitly.
        var now = _clock.Now;
        var windowEnd = now.Add(ReminderWindow);
        var dueExams = await context.Exams.IgnoreQueryFilters()
            .Include(e => e.Subject)
            .Include(e => e.Teacher)
            .Where(e => e.Status == ExamStatus.Published
                        && e.ReminderSentAt == null
                        && e.StartDate != null
                        && e.StartDate > now
                        && e.StartDate <= windowEnd)
            .ToListAsync(cancellationToken);

        foreach (var exam in dueExams)
        {
            // Sent from the exam's teacher (so replies reach them), or a school admin if
            // the teacher has no account.
            var senderUserId = exam.Teacher?.UserId;
            var senderName = exam.Teacher?.FullName ?? "School";
            var senderRole = "Teacher";
            if (senderUserId == null)
            {
                senderUserId = await context.UserRoles.IgnoreQueryFilters()
                    .Where(ur => ur.User.SchoolId == exam.SchoolId && ur.Role.Name == "Admin")
                    .Select(ur => (int?)ur.UserId)
                    .FirstOrDefaultAsync(cancellationToken);
                senderName = "School Admin";
                senderRole = "Admin";
            }
            if (senderUserId == null)
            {
                _logger.LogWarning("Exam {ExamId} has no teacher or admin account to send its reminder from.", exam.Id);
                continue;
            }

            var students = await context.Students.IgnoreQueryFilters()
                .Where(s => s.SchoolId == exam.SchoolId && s.ClassId == exam.ClassId && s.IsActive)
                .Select(s => new { s.Id, s.UserId })
                .ToListAsync(cancellationToken);
            var studentIds = students.Select(s => s.Id).ToList();
            var studentUserIds = students.Where(s => s.UserId != null).Select(s => s.UserId!.Value).Distinct().ToList();
            var parentUserIds = await context.ParentStudents.IgnoreQueryFilters()
                .Where(ps => ps.SchoolId == exam.SchoolId && studentIds.Contains(ps.StudentId))
                .Select(ps => ps.ParentUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var subject = $"{exam.KindLabel} Reminder: {exam.Title}";
            var body = ExamNotices.ScheduleText(exam, "starts in less than 24 hours") +
                       " Make sure you are ready and signed in on time. The exam cannot be opened before it starts.";

            var sender = (senderUserId.Value, senderName, senderRole);
            AddMessage(context, exam, sender, MessageAudience.Student, subject, body, studentUserIds);
            AddMessage(context, exam, sender, MessageAudience.Parent, subject, body, parentUserIds);

            exam.ReminderSentAt = now;
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Sent 24-hour reminder for exam {ExamId} to {Students} students and {Parents} parents.",
                exam.Id, studentUserIds.Count, parentUserIds.Count);
        }
    }

    private static void AddMessage(ApplicationDbContext context, Exam exam, (int UserId, string Name, string Role) sender, string audience,
        string subject, string body, List<int> recipientUserIds)
    {
        if (!recipientUserIds.Any()) return;

        context.Messages.Add(new Message
        {
            SchoolId = exam.SchoolId,
            SenderUserId = sender.UserId,
            SenderName = sender.Name,
            SenderRole = sender.Role,
            Audience = audience,
            Scope = MessageScope.Class,
            ClassId = exam.ClassId,
            Subject = subject,
            Body = body,
            Recipients = recipientUserIds
                .Select(uid => new MessageRecipient { SchoolId = exam.SchoolId, RecipientUserId = uid })
                .ToList()
        });
    }
}

public static class ExamNotices
{
    public static string When(Exam exam) => exam.StartDate == null
        ? "a date to be announced"
        : exam.StartDate.Value.ToString("dddd, MMMM d, yyyy 'at' h:mm tt");

    public static string ScheduleText(Exam exam, string verb)
    {
        var text = $"The {(exam.IsCaTest ? "CA test" : "exam")} \"{exam.Title}\" ({exam.Subject?.Name}) {verb}: {When(exam)}.";
        if (exam.DurationMinutes != null) text += $" It lasts {Duration(exam.DurationMinutes.Value)}.";
        return text;
    }

    public static string Duration(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0) return $"{minutes} minutes";
        var h = hours == 1 ? "1 hour" : $"{hours} hours";
        return rest == 0 ? h : $"{h} {rest} minutes";
    }
}
