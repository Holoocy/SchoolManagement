using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

// One row of a score template: a student and one subject they take.
public class ScoreTemplateRow
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public decimal? Assignment { get; set; }
    public decimal? Ca { get; set; }
    public decimal? Exam { get; set; }
}

public class ScoreImportResult
{
    public List<string> Errors { get; } = new();      // problems that stopped the whole upload
    public List<string> RowErrors { get; } = new();   // rows that were skipped
    public int RowsImported { get; set; }
    public int ScoresChanged { get; set; }
    public List<string> ColumnsFound { get; } = new();
}

// Builds score templates (CSV / Excel) and imports filled-in sheets.
public class ScoreSheetFiles
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxRows = 5000;

    private readonly ApplicationDbContext _context;
    private readonly ResultsService _results;

    public ScoreSheetFiles(ApplicationDbContext context, ResultsService results)
    {
        _context = context;
        _results = results;
    }

    private static string[] Headers(GradingScheme scheme) => new[]
    {
        "Student ID", "Student Name", "Grade", "Subject",
        $"Assignment (out of {scheme.AssignmentWeight})",
        $"CA (out of {scheme.CaWeight})",
        $"Exam (out of {scheme.ExamWeight})"
    };

    private static string Number(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    public static byte[] TemplateCsv(IEnumerable<ScoreTemplateRow> rows, GradingScheme scheme)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers(scheme).Select(Csv)));
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                r.StudentId, r.StudentName, r.Grade, r.Subject, Number(r.Assignment), Number(r.Ca), Number(r.Exam)
            }.Select(Csv)));
        }
        // BOM so Excel opens names with accents correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r', ';' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    public static byte[] TemplateXlsx(IEnumerable<ScoreTemplateRow> rows, GradingScheme scheme, string term)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Scores");
        var headers = Headers(scheme);
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];

        var header = sheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9EEF5");

        var row = 2;
        foreach (var r in rows)
        {
            // IDs stay text so leading zeros survive.
            sheet.Cell(row, 1).SetValue(r.StudentId);
            sheet.Cell(row, 1).Style.NumberFormat.Format = "@";
            sheet.Cell(row, 2).Value = r.StudentName;
            sheet.Cell(row, 3).Value = r.Grade;
            sheet.Cell(row, 4).Value = r.Subject;
            if (r.Assignment != null) sheet.Cell(row, 5).Value = r.Assignment.Value;
            if (r.Ca != null) sheet.Cell(row, 6).Value = r.Ca.Value;
            if (r.Exam != null) sheet.Cell(row, 7).Value = r.Exam.Value;
            row++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, headers.Length).AdjustToContents(1, Math.Max(1, row - 1));
        foreach (var col in new[] { 5, 6, 7 }) sheet.Column(col).Width = Math.Max(sheet.Column(col).Width, 18);

        var help = workbook.Worksheets.Add("How to fill");
        var lines = new[]
        {
            "How to fill this sheet",
            "",
            $"Term: {term}",
            $"Assignment is out of {scheme.AssignmentWeight}, CA is out of {scheme.CaWeight}, Exam is out of {scheme.ExamWeight} (total {scheme.Total}).",
            "Type each student's score in the Assignment, CA and Exam columns. Leave a cell empty to keep the score already in the app.",
            "You can delete a whole column (for example Assignment) if you are not uploading it.",
            "Students are matched by Student ID first, then by name. Subjects are matched by name or code.",
            "You can add more CA columns (e.g. \"1st CA\", \"2nd CA\"); they are added together and must not exceed the CA total.",
            "Don't change the Student ID or Subject columns. Save the file and upload it on the Score Sheet > Upload page."
        };
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Column(1).Width = 110;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ---------- import ----------

    private enum Col { Ignore, Id, Name, FirstName, LastName, Subject, Assignment, Ca, Exam }

    private static string Norm(string value) =>
        new string(Regex.Replace(value, @"\(.*?\)", "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static readonly Regex CaHeader = new(@"^(\d+(st|nd|rd|th)?|first|second|third)?(ca|test|catest)\d*(score|test|scores)?$");

    private static Col Classify(string header)
    {
        var h = Norm(header);
        if (h.Length == 0) return Col.Ignore;
        if (h is "studentid" or "id" or "admissionnumber" or "admissionno" or "admno" or "regno" or "registrationnumber"
            or "studentno" or "studentnumber" or "matricno") return Col.Id;
        if (h is "studentname" or "name" or "fullname" or "names" or "studentsname") return Col.Name;
        if (h is "firstname" or "firstnames" or "givenname") return Col.FirstName;
        if (h is "lastname" or "surname" or "familyname") return Col.LastName;
        if (h is "subject" or "subjectname" or "subjectcode" or "course") return Col.Subject;
        if (h.Contains("assignment") || h is "assign" || h.StartsWith("homework")) return Col.Assignment;
        if (h.Contains("continuousassessment") || CaHeader.IsMatch(h)) return Col.Ca;
        if (h.StartsWith("exam") || h.EndsWith("exam") || h is "examination" or "examscore") return Col.Exam;
        return Col.Ignore;
    }

    private static List<List<string>> ReadXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        var rows = new List<List<string>>();
        if (used == null) return rows;

        var lastCol = used.LastColumn().ColumnNumber();
        foreach (var r in sheet.Rows(1, used.LastRow().RowNumber()))
        {
            var cells = new List<string>();
            for (var c = 1; c <= lastCol; c++)
            {
                var value = r.Cell(c).Value;
                cells.Add(value.IsNumber ? value.GetNumber().ToString(CultureInfo.InvariantCulture)
                    : value.IsBlank ? string.Empty
                    : r.Cell(c).GetFormattedString());
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static bool TryScore(string raw, out decimal value)
    {
        raw = raw.Trim();
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return true;
        // "12,5" from a comma-decimal locale.
        return !raw.Contains('.') && raw.Count(c => c == ',') == 1
            && decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string NameKey(string value) =>
        string.Join(' ', value.ToLowerInvariant().Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public async Task<ScoreImportResult> ImportAsync(IFormFile? file, string term, int? defaultSubjectId,
        ResultsScope scope, GradingScheme scheme, int userId)
    {
        var result = new ScoreImportResult();

        if (file is not { Length: > 0 })
        {
            result.Errors.Add("Choose a CSV or Excel (.xlsx) file to upload.");
            return result;
        }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".csv" or ".xlsx"))
        {
            result.Errors.Add(ext == ".xls"
                ? "Old Excel .xls files can't be read. In Excel use File → Save As → Excel Workbook (.xlsx) or CSV, then upload that."
                : "The file must be a .csv or .xlsx file.");
            return result;
        }
        if (file.Length > MaxFileBytes)
        {
            result.Errors.Add("The file must be 5 MB or smaller.");
            return result;
        }

        List<List<string>> rows;
        try
        {
            if (ext == ".csv")
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                rows = StudentCsvImporter.ParseCsv(await reader.ReadToEndAsync());
            }
            else
            {
                await using var stream = file.OpenReadStream();
                rows = ReadXlsx(stream);
            }
        }
        catch (Exception)
        {
            result.Errors.Add("The file couldn't be read. Make sure it is a valid CSV or Excel (.xlsx) file and not password protected.");
            return result;
        }

        // The header is the first row that names at least one score column.
        var headerIndex = rows.FindIndex(r => r.Any(c => Classify(c) is Col.Assignment or Col.Ca or Col.Exam));
        if (headerIndex < 0)
        {
            result.Errors.Add("No score columns found. The first row needs headings such as Student ID, Student Name, Subject, Assignment, CA and Exam.");
            return result;
        }

        var cols = rows[headerIndex].Select(Classify).ToList();
        int IndexOf(Col c) => cols.IndexOf(c);
        var idCol = IndexOf(Col.Id);
        var nameCol = IndexOf(Col.Name);
        var firstCol = IndexOf(Col.FirstName);
        var lastCol = IndexOf(Col.LastName);
        var subjectCol = IndexOf(Col.Subject);
        var componentCols = new Dictionary<string, List<int>>
        {
            [ScoreComponents.Assignment] = cols.Select((c, i) => (c, i)).Where(x => x.c == Col.Assignment).Select(x => x.i).ToList(),
            [ScoreComponents.Ca] = cols.Select((c, i) => (c, i)).Where(x => x.c == Col.Ca).Select(x => x.i).ToList(),
            [ScoreComponents.Exam] = cols.Select((c, i) => (c, i)).Where(x => x.c == Col.Exam).Select(x => x.i).ToList()
        };
        foreach (var (component, list) in componentCols)
        {
            if (list.Count > 0) result.ColumnsFound.Add(component == ScoreComponents.Ca && list.Count > 1 ? $"CA ({list.Count} columns added together)" : component);
        }

        if (idCol < 0 && nameCol < 0 && (firstCol < 0 || lastCol < 0))
            result.Errors.Add("Add a \"Student ID\" or \"Student Name\" column so each row can be matched to a student.");
        if (subjectCol < 0 && defaultSubjectId == null)
            result.Errors.Add("Add a \"Subject\" column, or choose the subject on the upload page.");
        var dataRows = rows.Skip(headerIndex + 1).Select((r, i) => (Cells: r, Line: headerIndex + 2 + i))
            .Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (dataRows.Count == 0) result.Errors.Add("The file has no rows below the heading row.");
        if (dataRows.Count > MaxRows) result.Errors.Add($"The file has {dataRows.Count} rows. Upload at most {MaxRows} at a time.");
        foreach (var (component, list) in componentCols)
        {
            if (list.Count > 0 && scheme.WeightOf(component) == 0)
                result.Errors.Add($"{(component == ScoreComponents.Ca ? "CA" : component)} is worth 0 marks in the grading setup, so its column can't be uploaded. Remove the column or change the grading setup.");
        }
        if (result.Errors.Any()) return result;

        // Students and subjects the user may record scores for.
        var students = await _context.Students
            .Where(s => s.ClassId != null && scope.ClassIds.Contains(s.ClassId.Value) || scope.IsAdmin)
            .Select(s => new { s.Id, s.AdmissionNumber, s.FirstName, s.LastName })
            .ToListAsync();
        var byId = students.GroupBy(s => s.AdmissionNumber.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);
        var byName = new Dictionary<string, List<int>>();
        foreach (var s in students)
        {
            foreach (var key in new[] { NameKey($"{s.FirstName} {s.LastName}"), NameKey($"{s.LastName} {s.FirstName}") }.Distinct())
            {
                if (!byName.TryGetValue(key, out var list)) byName[key] = list = new List<int>();
                if (!list.Contains(s.Id)) list.Add(s.Id);
            }
        }

        var subjects = await _context.Subjects.Where(s => scope.SubjectIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Code }).ToListAsync();
        var allSubjectNames = await _context.Subjects.Select(s => new { s.Name, s.Code }).ToListAsync();

        var recorded = await _results.LoadRecordedAsync(students.Select(s => s.Id).ToList(), term);
        var seen = new Dictionary<(int, int), int>();

        foreach (var (cells, line) in dataRows)
        {
            string Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : string.Empty;

            // Student: by ID, then by name.
            int? studentId = null;
            var id = Cell(idCol);
            var name = nameCol >= 0 ? Cell(nameCol) : $"{Cell(firstCol)} {Cell(lastCol)}".Trim();
            var label = name.Length > 0 ? name : id;
            if (id.Length > 0 && byId.TryGetValue(id.ToLowerInvariant(), out var sid)) studentId = sid;
            else if (name.Length > 0 && byName.TryGetValue(NameKey(name), out var matches))
            {
                if (matches.Count > 1)
                {
                    result.RowErrors.Add($"Row {line}: more than one student is called \"{name}\". Add their Student ID.");
                    continue;
                }
                studentId = matches[0];
            }
            if (studentId == null)
            {
                result.RowErrors.Add(id.Length == 0 && name.Length == 0
                    ? $"Row {line}: no Student ID or name."
                    : $"Row {line}: student \"{label}\" was not found{(scope.IsAdmin ? "" : " in your grades")}.");
                continue;
            }

            // Subject: by name or code, or the subject chosen on the page.
            int? subjectId = defaultSubjectId;
            var subjectText = Cell(subjectCol);
            if (subjectText.Length > 0)
            {
                var key = Norm(subjectText);
                var match = subjects.Where(s => Norm(s.Name) == key || Norm(s.Code) == key).ToList();
                if (match.Count != 1)
                {
                    result.RowErrors.Add(match.Count > 1
                        ? $"Row {line}: more than one subject matches \"{subjectText}\". Use the subject code."
                        : !scope.IsAdmin && allSubjectNames.Any(s => Norm(s.Name) == key || Norm(s.Code) == key)
                            ? $"Row {line}: \"{subjectText}\" is not one of your subjects."
                            : $"Row {line}: subject \"{subjectText}\" was not found.");
                    continue;
                }
                subjectId = match[0].Id;
            }
            else if (subjectCol >= 0 && defaultSubjectId == null)
            {
                result.RowErrors.Add($"Row {line}: no subject for {label}.");
                continue;
            }
            if (subjectId == null || !scope.SubjectIds.Contains(subjectId.Value))
            {
                result.RowErrors.Add($"Row {line}: you can't record scores for that subject.");
                continue;
            }

            if (seen.TryGetValue((studentId.Value, subjectId.Value), out var firstLine))
            {
                result.RowErrors.Add($"Row {line}: {label} already appears for this subject on row {firstLine}; this row was skipped.");
                continue;
            }

            // Scores: blank cells keep what's in the app; several CA columns are added together.
            var values = new Dictionary<string, decimal>();
            var problems = new List<string>();
            foreach (var (component, list) in componentCols)
            {
                if (list.Count == 0) continue;
                var label2 = component == ScoreComponents.Ca ? "CA" : component;
                var filled = list.Select(Cell).Where(v => v.Length > 0).ToList();
                if (filled.Count == 0) continue;

                decimal sum = 0;
                var ok = true;
                foreach (var raw in filled)
                {
                    if (!TryScore(raw, out var v) || v < 0) { problems.Add($"{label2} \"{raw}\" is not a valid score"); ok = false; break; }
                    sum += v;
                }
                if (!ok) continue;

                var weight = scheme.WeightOf(component);
                if (sum > weight) { problems.Add($"{label2} {sum.ToString("0.##", CultureInfo.InvariantCulture)} is more than {weight}"); continue; }
                values[component] = Math.Round(sum, 2);
            }
            if (problems.Any())
            {
                result.RowErrors.Add($"Row {line} ({label}): {string.Join("; ", problems)}. Row skipped.");
                continue;
            }
            if (values.Count == 0) continue; // nothing to record on this row

            seen[(studentId.Value, subjectId.Value)] = line;
            foreach (var (component, value) in values)
            {
                if (_results.SetRecorded(recorded, studentId.Value, subjectId.Value, term, component, value, scheme, userId))
                    result.ScoresChanged++;
            }
            result.RowsImported++;
        }

        await _context.SaveChangesAsync();
        return result;
    }
}
