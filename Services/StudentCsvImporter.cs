using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

// Reads a CSV of student details and enrolls every row, or none of them if any row
// has a problem (so a file can be fixed and uploaded again without creating duplicates).
public class StudentCsvImporter
{
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxRows = 1000;

    public static readonly string[] Columns =
        { "FirstName", "LastName", "DateOfBirth", "Gender", "Email", "Phone", "Address", "Grade", "EnrollmentDate", "AcademicCalendar" };

    public static readonly string[] RequiredColumns = { "FirstName", "LastName", "DateOfBirth", "Gender", "Email", "Address" };

    private static readonly string[] DateFormats =
        { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d MMM yyyy", "d MMMM yyyy" };

    private readonly ApplicationDbContext _context;

    public StudentCsvImporter(ApplicationDbContext context)
    {
        _context = context;
    }

    public static byte[] TemplateFile()
    {
        var csv = string.Join(",", Columns) + "\r\n" +
                  "Ada,Okafor,2014-03-21,Female,ada.okafor@example.com,08012345678,\"12 Unity Road, Ikeja\",Grade 4,,2026/2027\r\n";
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
    }

    /// <param name="allowedClassIds">Grades the uploader may enroll into; null means any grade.</param>
    /// <param name="defaultClassId">Grade used for rows with an empty Grade column.</param>
    /// <param name="gradeRequired">Whether every student must end up in a grade.</param>
    public async Task<StudentImportResult> ImportAsync(IFormFile? file, IReadOnlyCollection<int>? allowedClassIds,
        int? defaultClassId, bool gradeRequired)
    {
        var result = new StudentImportResult();

        if (file is not { Length: > 0 })
        {
            result.Errors.Add("Choose a CSV file to upload.");
            return result;
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("The file must be a .csv file. In Excel, use File → Save As → CSV (Comma delimited).");
            return result;
        }
        if (file.Length > MaxFileBytes)
        {
            result.Errors.Add("The file must be 2 MB or smaller.");
            return result;
        }

        List<List<string>> rows;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            rows = ParseCsv(await reader.ReadToEndAsync());
        }
        rows.RemoveAll(r => r.All(string.IsNullOrWhiteSpace));

        if (rows.Count < 2)
        {
            result.Errors.Add("The file has no student rows. Keep the header row and add one student per row below it.");
            return result;
        }
        if (rows.Count - 1 > MaxRows)
        {
            result.Errors.Add($"The file has {rows.Count - 1} students. Upload at most {MaxRows} at a time.");
            return result;
        }

        // Match headers loosely: "First Name", "first_name" and "FirstName" are the same column.
        var header = rows[0].Select(Normalize).ToList();
        var columnIndex = new Dictionary<string, int>();
        foreach (var column in Columns)
        {
            var i = header.IndexOf(Normalize(column));
            if (i < 0 && column == "Grade") i = header.FindIndex(h => h is "class" or "classname" or "gradename");
            if (i >= 0) columnIndex[column] = i;
        }
        var missing = RequiredColumns.Where(c => !columnIndex.ContainsKey(c)).ToList();
        if (missing.Any())
        {
            result.Errors.Add($"The header row is missing: {string.Join(", ", missing)}. Download the template to see the expected columns.");
            return result;
        }

        var classes = await _context.Classes.Select(c => new { c.Id, c.Name }).ToListAsync();
        if (defaultClassId != null && classes.All(c => c.Id != defaultClassId))
        {
            result.Errors.Add("The grade chosen for the whole file doesn't exist.");
            return result;
        }
        // Several grades can share a name, so a name only counts if it points to exactly
        // one grade the uploader may use.
        var classesByName = classes
            .GroupBy(c => Normalize(c.Name))
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());
        var existingEmails = (await _context.Students.Select(s => s.Email).ToListAsync())
            .Select(e => e.Trim().ToLowerInvariant())
            .ToHashSet();
        var existingNumbers = (await _context.Students.Select(s => s.AdmissionNumber).ToListAsync()).ToHashSet();
        var emailsInFile = new Dictionary<string, int>();

        for (var r = 1; r < rows.Count; r++)
        {
            var rowNumber = r + 1; // as shown in a spreadsheet, counting the header
            var row = rows[r];
            string Get(string column) =>
                columnIndex.TryGetValue(column, out var i) && i < row.Count ? row[i].Trim() : string.Empty;

            var errors = new List<string>();
            var student = new Student
            {
                FirstName = Get("FirstName"),
                LastName = Get("LastName"),
                Email = Get("Email"),
                Phone = NullIfEmpty(Get("Phone")),
                Address = Get("Address"),
                AcademicCalendar = NullIfEmpty(Get("AcademicCalendar")),
                IsActive = true
            };

            if (student.FirstName.Length == 0) errors.Add("FirstName is empty");
            if (student.LastName.Length == 0) errors.Add("LastName is empty");
            if (student.Address.Length == 0) errors.Add("Address is empty");

            if (TryParseDate(Get("DateOfBirth"), out var dob)) student.DateOfBirth = dob;
            else errors.Add(Get("DateOfBirth").Length == 0 ? "DateOfBirth is empty" : $"DateOfBirth \"{Get("DateOfBirth")}\" is not a date (use YYYY-MM-DD or DD/MM/YYYY)");

            var enrollment = Get("EnrollmentDate");
            if (enrollment.Length == 0) student.EnrollmentDate = DateTime.Today;
            else if (TryParseDate(enrollment, out var enrolled)) student.EnrollmentDate = enrolled;
            else errors.Add($"EnrollmentDate \"{enrollment}\" is not a date");

            var gender = Get("Gender").ToLowerInvariant();
            student.Gender = gender switch { "male" or "m" => "Male", "female" or "f" => "Female", _ => string.Empty };
            if (student.Gender.Length == 0) errors.Add(gender.Length == 0 ? "Gender is empty" : $"Gender \"{Get("Gender")}\" must be Male or Female");

            var emailKey = student.Email.ToLowerInvariant();
            if (student.Email.Length == 0) errors.Add("Email is empty");
            else if (!new EmailAddressAttribute().IsValid(student.Email)) errors.Add($"Email \"{student.Email}\" is not valid");
            else if (existingEmails.Contains(emailKey)) errors.Add($"a student with email {student.Email} is already enrolled");
            else if (emailsInFile.TryGetValue(emailKey, out var firstRow)) errors.Add($"email {student.Email} is also used on row {firstRow}");
            else emailsInFile[emailKey] = rowNumber;

            if (student.Phone != null && !new PhoneAttribute().IsValid(student.Phone)) errors.Add($"Phone \"{student.Phone}\" is not valid");

            var gradeName = Get("Grade");
            if (gradeName.Length > 0)
            {
                var matches = classesByName.GetValueOrDefault(Normalize(gradeName)) ?? new List<int>();
                var usable = allowedClassIds == null ? matches : matches.Where(allowedClassIds.Contains).ToList();
                if (usable.Count == 1) student.ClassId = usable[0];
                else if (matches.Count == 0) errors.Add($"grade \"{gradeName}\" doesn't exist");
                else if (usable.Count == 0) errors.Add($"you can't enroll students into {gradeName}");
                else errors.Add($"more than one grade is called \"{gradeName}\" (leave Grade empty and choose the grade for the whole file instead)");
            }
            else
            {
                student.ClassId = defaultClassId;
                if (student.ClassId == null && gradeRequired)
                    errors.Add("no grade given (fill in the Grade column or choose a grade for the whole file)");
            }

            if (errors.Any())
            {
                result.Errors.Add($"Row {rowNumber}: {string.Join("; ", errors)}.");
                continue;
            }

            student.AdmissionNumber = NewAdmissionNumber(existingNumbers);
            result.Students.Add(student);
        }

        if (result.Errors.Any())
        {
            result.Students.Clear();
            return result;
        }

        _context.Students.AddRange(result.Students);
        await _context.SaveChangesAsync();
        return result;
    }

    private static string NewAdmissionNumber(HashSet<string> taken)
    {
        string code;
        do
        {
            code = Random.Shared.Next(100000, 999999).ToString();
        } while (!taken.Add(code));
        return code;
    }

    private static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // RFC 4180 CSV: commas or semicolons, quoted fields, doubled quotes, newlines in quotes.
    internal static List<List<string>> ParseCsv(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = new List<string>();
            }
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}

public class StudentImportResult
{
    public List<string> Errors { get; } = new();
    public List<Student> Students { get; } = new();
    public bool Succeeded => !Errors.Any() && Students.Any();
}
