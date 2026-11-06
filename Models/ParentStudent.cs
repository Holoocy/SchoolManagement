namespace SchoolManagement.Models;

public class ParentStudent : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int ParentUserId { get; set; }
    public User? ParentUser { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }
}
