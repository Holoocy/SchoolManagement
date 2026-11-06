using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class Student : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    [Required]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Admission Number")]
    public string AdmissionNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Date of Birth")]
    [DataType(DataType.Date)]
    public DateTime DateOfBirth { get; set; }

    [Required]
    public string Gender { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone]
    public string? Phone { get; set; }

    [Required]
    public string Address { get; set; } = string.Empty;

    [Display(Name = "Enrollment Date")]
    [DataType(DataType.Date)]
    public DateTime EnrollmentDate { get; set; } = DateTime.UtcNow;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Class")]
    public int? ClassId { get; set; }
    public Class? Class { get; set; }

    [Display(Name = "Academic Calendar")]
    public string? AcademicCalendar { get; set; }

    [Display(Name = "Photo")]
    public string? PhotoPath { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    public ICollection<StudentSubject> StudentSubjects { get; set; } = new List<StudentSubject>();
    public ICollection<AssignmentSubmission> AssignmentSubmissions { get; set; } = new List<AssignmentSubmission>();

    public string FullName => $"{FirstName} {LastName}";
}
