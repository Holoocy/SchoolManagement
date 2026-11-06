using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class Class : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    [Required]
    [Display(Name = "Class Name")]
    public string Name { get; set; } = string.Empty;

    public ICollection<Student> Students { get; set; } = new List<Student>();
    public ICollection<TeacherClass> TeacherClasses { get; set; } = new List<TeacherClass>();
    public ICollection<SubjectClass> SubjectClasses { get; set; } = new List<SubjectClass>();
}
