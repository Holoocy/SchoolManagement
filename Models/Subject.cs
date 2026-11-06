using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class Subject : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    [Required]
    [Display(Name = "Subject Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Subject Code")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Range(1, 10)]
    [Display(Name = "Credits")]
    public int Credits { get; set; } = 3;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public ICollection<SubjectTeacher> SubjectTeachers { get; set; } = new List<SubjectTeacher>();
    public ICollection<SubjectClass> SubjectClasses { get; set; } = new List<SubjectClass>();
    public ICollection<StudentSubject> StudentSubjects { get; set; } = new List<StudentSubject>();
}
