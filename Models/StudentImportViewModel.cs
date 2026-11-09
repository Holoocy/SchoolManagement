namespace SchoolManagement.Models;

public class StudentImportViewModel
{
    // Grade for rows whose Grade column is empty.
    public int? DefaultClassId { get; set; }

    // Teachers must put every student in one of their grades; admins may leave it blank.
    public bool GradeRequired { get; set; }

    public string BackController { get; set; } = "Students";
    public string BackAction { get; set; } = "Index";

    public List<string> Errors { get; set; } = new();
}
