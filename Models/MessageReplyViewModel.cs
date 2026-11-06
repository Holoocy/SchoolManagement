using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class MessageReplyViewModel
{
    public int OriginalMessageRecipientId { get; set; }
    public string ToName { get; set; } = string.Empty;
    public string OriginalSubject { get; set; } = string.Empty;
    public string OriginalBody { get; set; } = string.Empty;

    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;
}
