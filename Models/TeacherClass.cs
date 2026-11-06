namespace SchoolManagement.Models;

public class TeacherClass : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    public int ClassId { get; set; }
    public Class? Class { get; set; }
}
