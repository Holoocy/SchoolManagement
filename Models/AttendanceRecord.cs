using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class AttendanceRecord : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    [Required]
    [Display(Name = "Student")]
    public int StudentId { get; set; }

    public Student? Student { get; set; }

    [Required]
    [Display(Name = "Date")]
    [DataType(DataType.Date)]
    public DateTime AttendanceDate { get; set; } = DateTime.UtcNow;

    [Required]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Present";

    [Display(Name = "Remarks")]
    public string? Remarks { get; set; }
}
