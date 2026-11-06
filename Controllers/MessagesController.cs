using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin,Teacher,Parent,Student")]
public class MessagesController : Controller
{
    private readonly ApplicationDbContext _context;

    public MessagesController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentRole =>
        User.IsInRole("Admin") ? "Admin" :
        User.IsInRole("Teacher") ? "Teacher" : "Parent";

    public async Task<IActionResult> Index()
    {
        var inbox = await _context.MessageRecipients
            .Include(r => r.Message).ThenInclude(m => m!.Class)
            .Where(r => r.RecipientUserId == CurrentUserId)
            .OrderByDescending(r => r.Message!.SentAt)
            .ToListAsync();

        return View(inbox);
    }

    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> Sent()
    {
        var sent = await _context.Messages
            .Include(m => m.Class)
            .Include(m => m.Recipients)
            .Where(m => m.SenderUserId == CurrentUserId)
            .OrderByDescending(m => m.SentAt)
            .ToListAsync();

        return View(sent);
    }

    public async Task<IActionResult> Details(int id)
    {
        var recipient = await _context.MessageRecipients
            .Include(r => r.Message).ThenInclude(m => m!.Class)
            .FirstOrDefaultAsync(r => r.Id == id && r.RecipientUserId == CurrentUserId);

        if (recipient == null)
        {
            return NotFound();
        }

        if (!recipient.IsRead)
        {
            recipient.IsRead = true;
            recipient.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return View(recipient);
    }

    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> SentDetails(int id)
    {
        var message = await _context.Messages
            .Include(m => m.Class)
            .Include(m => m.Recipients).ThenInclude(r => r.RecipientUser)
            .FirstOrDefaultAsync(m => m.Id == id && m.SenderUserId == CurrentUserId);

        if (message == null)
        {
            return NotFound();
        }

        return View(message);
    }

    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> Reply(int id)
    {
        var recipient = await _context.MessageRecipients
            .Include(r => r.Message).ThenInclude(m => m!.SenderUser)
            .FirstOrDefaultAsync(r => r.Id == id && r.RecipientUserId == CurrentUserId);

        if (recipient?.Message == null)
        {
            return NotFound();
        }

        var model = new MessageReplyViewModel
        {
            OriginalMessageRecipientId = recipient.Id,
            ToName = recipient.Message.SenderName,
            OriginalSubject = recipient.Message.Subject,
            OriginalBody = recipient.Message.Body,
            Subject = recipient.Message.Subject.StartsWith("Re: ") ? recipient.Message.Subject : $"Re: {recipient.Message.Subject}"
        };

        return View(model);
    }

    [Authorize(Roles = "Admin,Teacher,Parent")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(MessageReplyViewModel model)
    {
        var recipient = await _context.MessageRecipients
            .Include(r => r.Message).ThenInclude(m => m!.SenderUser)
            .FirstOrDefaultAsync(r => r.Id == model.OriginalMessageRecipientId && r.RecipientUserId == CurrentUserId);

        if (recipient?.Message == null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            model.ToName = recipient.Message.SenderName;
            model.OriginalSubject = recipient.Message.Subject;
            model.OriginalBody = recipient.Message.Body;
            return View(model);
        }

        var reply = new Message
        {
            SenderUserId = CurrentUserId,
            SenderName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Unknown",
            SenderRole = CurrentRole,
            Audience = recipient.Message.SenderRole,
            Scope = MessageScope.Individual,
            Subject = model.Subject.Trim(),
            Body = model.Body.Trim(),
            Recipients = new List<MessageRecipient>
            {
                new MessageRecipient { RecipientUserId = recipient.Message.SenderUserId }
            }
        };

        _context.Messages.Add(reply);
        await _context.SaveChangesAsync();

        TempData["MessageSuccess"] = $"Reply sent to {recipient.Message.SenderName}.";
        return RedirectToAction(nameof(Sent));
    }

    [Authorize(Roles = "Admin,Teacher,Parent")]
    public async Task<IActionResult> Compose()
    {
        var model = new MessageComposeViewModel();
        await PopulateOptionsAsync(model);
        model.Audience = model.AvailableAudiences.First();
        return View(model);
    }

    [Authorize(Roles = "Admin,Teacher,Parent")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Compose(MessageComposeViewModel model)
    {
        var recipientUserIds = await ResolveRecipientsAsync(model);

        if (recipientUserIds == null)
        {
            ModelState.AddModelError(string.Empty, "That selection isn't available to you.");
        }
        else if (recipientUserIds.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "No recipients were found for that selection.");
        }

        if (!ModelState.IsValid || recipientUserIds == null || recipientUserIds.Count == 0)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        var isBroadcastScope = model.Audience != MessageAudience.Admin &&
            (model.Scope == MessageScope.Class || model.Scope == MessageScope.School);

        var senderName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Unknown";
        var subject = !string.IsNullOrWhiteSpace(model.RegardingSubject)
            ? model.RegardingSubject.Trim()
            : $"Message from {senderName}";

        var message = new Message
        {
            SenderUserId = CurrentUserId,
            SenderName = senderName,
            SenderRole = CurrentRole,
            Audience = model.Audience,
            Scope = isBroadcastScope ? model.Scope : MessageScope.Individual,
            ClassId = model.Audience != MessageAudience.Admin && model.Scope == MessageScope.Class ? model.ClassId : null,
            Subject = subject,
            Body = model.Body.Trim()
        };

        message.Recipients = recipientUserIds
            .Select(userId => new MessageRecipient { RecipientUserId = userId })
            .ToList();

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        TempData["MessageSuccess"] = $"Message sent to {recipientUserIds.Count} recipient(s).";
        return RedirectToAction(nameof(Sent));
    }

    private List<string> GetAvailableAudiences()
    {
        return CurrentRole switch
        {
            "Admin" => new List<string> { MessageAudience.Parent, MessageAudience.Teacher },
            "Teacher" => new List<string> { MessageAudience.Parent, MessageAudience.Admin },
            _ => new List<string> { MessageAudience.Teacher, MessageAudience.Admin }
        };
    }

    private async Task<List<int>?> ResolveRecipientsAsync(MessageComposeViewModel model)
    {
        var available = GetAvailableAudiences();
        if (!available.Contains(model.Audience))
        {
            return null;
        }

        // Messaging "Admin" always reaches the whole administration - no need to pick a specific person.
        if (model.Audience == MessageAudience.Admin)
        {
            var admins = await GetAdminOptionsAsync();
            return admins.Select(o => int.Parse(o.Value!)).ToList();
        }

        if (CurrentRole == "Parent")
        {
            // The only other audience for Parent is Teacher, always an individual pick.
            var allowed = await GetParentTeacherOptionsAsync();
            if (model.RecipientUserId == null || !allowed.Any(o => o.Value == model.RecipientUserId.Value.ToString()))
            {
                return null;
            }

            return new List<int> { model.RecipientUserId.Value };
        }

        if (CurrentRole == "Teacher")
        {
            // The only other audience for Teacher is Parent, restricted to their own classes.
            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
            if (teacher == null) return new List<int>();

            if (model.Scope == MessageScope.Class)
            {
                var teacherClassIds = await _context.TeacherClasses
                    .Where(tc => tc.TeacherId == teacher.Id).Select(tc => tc.ClassId).ToListAsync();
                if (model.ClassId == null || !teacherClassIds.Contains(model.ClassId.Value)) return null;

                var studentIds = await _context.Students.Where(s => s.ClassId == model.ClassId).Select(s => s.Id).ToListAsync();
                return await _context.ParentStudents.Where(ps => studentIds.Contains(ps.StudentId))
                    .Select(ps => ps.ParentUserId).Distinct().ToListAsync();
            }

            var allowedParents = await GetTeacherParentOptionsAsync(teacher.Id);
            if (model.RecipientUserId == null || !allowedParents.Any(o => o.Value == model.RecipientUserId.Value.ToString()))
            {
                return null;
            }

            return new List<int> { model.RecipientUserId.Value };
        }

        // Admin
        if (model.Audience == MessageAudience.Parent)
        {
            if (model.Scope == MessageScope.School)
            {
                return await _context.ParentStudents.Select(ps => ps.ParentUserId).Distinct().ToListAsync();
            }

            if (model.Scope == MessageScope.Class)
            {
                if (model.ClassId == null) return new List<int>();
                var studentIds = await _context.Students.Where(s => s.ClassId == model.ClassId).Select(s => s.Id).ToListAsync();
                return await _context.ParentStudents.Where(ps => studentIds.Contains(ps.StudentId))
                    .Select(ps => ps.ParentUserId).Distinct().ToListAsync();
            }

            if (model.RecipientUserId == null) return new List<int>();
            var isValidParent = await _context.ParentStudents.AnyAsync(ps => ps.ParentUserId == model.RecipientUserId.Value);
            return isValidParent ? new List<int> { model.RecipientUserId.Value } : new List<int>();
        }

        if (model.Audience == MessageAudience.Teacher)
        {
            if (model.Scope == MessageScope.School)
            {
                return await _context.Teachers.Where(t => t.UserId != null).Select(t => t.UserId!.Value).Distinct().ToListAsync();
            }

            if (model.Scope == MessageScope.Class)
            {
                if (model.ClassId == null) return new List<int>();
                return await _context.TeacherClasses.Where(tc => tc.ClassId == model.ClassId)
                    .Join(_context.Teachers, tc => tc.TeacherId, t => t.Id, (tc, t) => t.UserId)
                    .Where(userId => userId != null).Select(userId => userId!.Value)
                    .Distinct().ToListAsync();
            }

            if (model.RecipientUserId == null) return new List<int>();
            var isValidTeacher = await _context.Teachers.AnyAsync(t => t.UserId == model.RecipientUserId.Value);
            return isValidTeacher ? new List<int> { model.RecipientUserId.Value } : new List<int>();
        }

        return null;
    }

    private async Task PopulateOptionsAsync(MessageComposeViewModel model)
    {
        model.AvailableAudiences = GetAvailableAudiences();

        if (CurrentRole == "Parent")
        {
            model.TeacherOptions = await GetParentTeacherOptionsAsync();
            model.TeacherSubjectNames = await GetTeacherSubjectNamesAsync(
                model.TeacherOptions.Select(o => int.Parse(o.Value!)).ToList());
            return;
        }

        if (CurrentRole == "Teacher")
        {
            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
            if (teacher == null) return;

            var teacherClassIds = await _context.TeacherClasses
                .Where(tc => tc.TeacherId == teacher.Id).Select(tc => tc.ClassId).ToListAsync();

            model.ClassOptions = await _context.Classes.Where(c => teacherClassIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync();

            model.ParentOptions = await GetTeacherParentOptionsAsync(teacher.Id);
            return;
        }

        // Admin
        model.CanSendToSchool = true;
        model.ClassOptions = await _context.Classes.OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync();

        var parentUserIds = await _context.ParentStudents.Select(ps => ps.ParentUserId).Distinct().ToListAsync();
        model.ParentOptions = await _context.Users
            .Where(u => parentUserIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.FullName })
            .ToListAsync();

        model.TeacherOptions = await _context.Teachers.Where(t => t.UserId != null)
            .OrderBy(t => t.FirstName)
            .Select(t => new SelectListItem { Value = t.UserId!.Value.ToString(), Text = t.FirstName + " " + t.LastName })
            .ToListAsync();

    }

    private async Task<Dictionary<int, string>> GetTeacherSubjectNamesAsync(List<int> teacherUserIds)
    {
        var teachers = await _context.Teachers
            .Where(t => t.UserId != null && teacherUserIds.Contains(t.UserId.Value))
            .Select(t => new { t.Id, t.UserId })
            .ToListAsync();
        var teacherIds = teachers.Select(t => t.Id).ToList();

        var subjectTeachers = await _context.SubjectTeachers
            .Include(st => st.Subject)
            .Where(st => teacherIds.Contains(st.TeacherId))
            .ToListAsync();

        var result = new Dictionary<int, string>();
        foreach (var t in teachers)
        {
            var names = subjectTeachers.Where(st => st.TeacherId == t.Id).Select(st => st.Subject!.Name);
            var joined = string.Join(", ", names);
            if (!string.IsNullOrEmpty(joined))
            {
                result[t.UserId!.Value] = joined;
            }
        }

        return result;
    }

    private async Task<List<SelectListItem>> GetAdminOptionsAsync()
    {
        var adminUserIds = await _context.UserRoles
            .Where(ur => ur.Role!.Name == "Admin")
            .Select(ur => ur.UserId)
            .ToListAsync();

        return await _context.Users
            .Where(u => adminUserIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.FullName })
            .ToListAsync();
    }

    private async Task<List<SelectListItem>> GetParentTeacherOptionsAsync()
    {
        var childIds = await _context.ParentStudents
            .Where(ps => ps.ParentUserId == CurrentUserId)
            .Select(ps => ps.StudentId)
            .ToListAsync();

        var classIds = await _context.Students
            .Where(s => childIds.Contains(s.Id) && s.ClassId != null)
            .Select(s => s.ClassId!.Value)
            .Distinct()
            .ToListAsync();

        var teacherIds = await _context.TeacherClasses
            .Where(tc => classIds.Contains(tc.ClassId))
            .Select(tc => tc.TeacherId)
            .Distinct()
            .ToListAsync();

        return await _context.Teachers
            .Where(t => teacherIds.Contains(t.Id) && t.UserId != null)
            .OrderBy(t => t.FirstName)
            .Select(t => new SelectListItem { Value = t.UserId!.Value.ToString(), Text = $"{t.FirstName} {t.LastName}" })
            .ToListAsync();
    }

    private async Task<List<SelectListItem>> GetTeacherParentOptionsAsync(int teacherId)
    {
        var teacherClassIds = await _context.TeacherClasses
            .Where(tc => tc.TeacherId == teacherId).Select(tc => tc.ClassId).ToListAsync();

        var teacherStudentIds = await _context.Students
            .Where(s => s.ClassId != null && teacherClassIds.Contains(s.ClassId.Value))
            .Select(s => s.Id).ToListAsync();

        var teacherParentUserIds = await _context.ParentStudents
            .Where(ps => teacherStudentIds.Contains(ps.StudentId))
            .Select(ps => ps.ParentUserId).Distinct().ToListAsync();

        return await _context.Users
            .Where(u => teacherParentUserIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.FullName })
            .ToListAsync();
    }
}
