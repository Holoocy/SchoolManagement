namespace SchoolManagement.Models;

public class SubjectClass : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int ClassId { get; set; }
    public Class? Class { get; set; }
}
