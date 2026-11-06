namespace SchoolManagement.Models;

public class MessageRecipient : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int MessageId { get; set; }
    public Message? Message { get; set; }

    public int RecipientUserId { get; set; }
    public User? RecipientUser { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
