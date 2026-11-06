using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public static class MessageScope
{
    public const string Individual = "Individual";
    public const string Class = "Class";
    public const string School = "School";
}

public class Message : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int SenderUserId { get; set; }
    public User? SenderUser { get; set; }

    [Required]
    public string SenderName { get; set; } = string.Empty;

    [Required]
    public string SenderRole { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = MessageAudience.Parent;

    [Required]
    public string Scope { get; set; } = MessageScope.Individual;

    public int? ClassId { get; set; }
    public Class? Class { get; set; }

    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public ICollection<MessageRecipient> Recipients { get; set; } = new List<MessageRecipient>();

    public string RecipientSummary
    {
        get
        {
            if (Scope == MessageScope.School) return $"Entire School ({Audience}s)";
            if (Scope == MessageScope.Class) return $"Class: {Class?.Name} ({Audience}s)";
            return "Direct message";
        }
    }
}
