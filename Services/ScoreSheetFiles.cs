using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

// A score template: one row per student, one column per subject and part
// (e.g. "Mathematics Assignment", "Mathematics CA", "Mathematics Exam").
public class ScoreTemplate
{
    public List<ScoreTemplateSubject> Subjects { get; set; } = new();
    public List<ScoreTemplateStudent> Students { get; set; } = new();
}

public class ScoreTemplateSubject
{
    public int Id { get; set; }
    public string Header { get; set; } = string.Empty;   // subject name (plus code if two share a name)
}

public class ScoreTemplateStudent
{
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public HashSet<int> Takes { get; set; } = new();       // subjects the student takes; others are greyed out
    public Dictionary<(int SubjectId, string Component), decimal> Scores { get; set; } = new();
}

public class ScoreImportResult
{
    public List<string> Errors { get; } = new();      // problems that stopped the whole upload
    public List<string> RowErrors { get; } = new();   // rows that were skipped
    public int RowsImported { get; set; }
    public int ScoresChanged { get; set; }
    public List<string> ColumnsFound { get; } = new();
    public List<string> ColumnWarnings { get; } = new(); // columns that were skipped
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

    private static readonly string[] IdentityHeaders = { "Student ID", "Student Name", "Grade" };

    private static string PartName(string component) => component == ScoreComponents.Ca ? "CA" : component;

    // Score columns in order: each subject's Assignment, CA and Exam (parts worth 0 are left out).
    private static List<(int SubjectId, string Component, string Header, int Weight)> ScoreColumns(ScoreTemplate template, GradingScheme scheme)
    {
        var parts = new[] { ScoreComponents.Assignment, ScoreComponents.Ca, ScoreComponents.Exam }
            .Where(c => scheme.WeightOf(c) > 0).ToList();
        return template.Subjects.SelectMany(sub => parts.Select(c =>
            (sub.Id, c, $"{sub.Header} {PartName(c)} (out of {scheme.WeightOf(c)})", scheme.WeightOf(c)))).ToList();
    }

    private static string Number(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    public static byte[] TemplateCsv(ScoreTemplate template, GradingScheme scheme)
    {
        var columns = ScoreColumns(template, scheme);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", IdentityHeaders.Concat(columns.Select(c => c.Header)).Select(Csv)));
        foreach (var st in template.Students)
        {
            var cells = new List<string> { st.StudentId, st.StudentName, st.Grade };
            cells.AddRange(columns.Select(c => st.Scores.TryGetValue((c.SubjectId, c.Component), out var v) ? Number(v) : string.Empty));
            sb.AppendLine(string.Join(",", cells.Select(Csv)));
        }
        // BOM so Excel opens names with accents correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r', ';' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    // (heading, column) colours, one pair per subject in turn.
    private static readonly (string Strong, string Light)[] SubjectColours =
    {
        ("#9DC3E6", "#EAF2FA"), // blue
        ("#A9D18E", "#EEF6E8"), // green
        ("#F4B183", "#FDF0E7"), // orange
        ("#C9B3E6", "#F3EEF9"), // purple
        ("#FFD966", "#FFF8E1"), // yellow
        ("#8FD3CC", "#E8F6F5"), // teal
        ("#F2A7C6", "#FCEDF3"), // pink
        ("#D6C49A", "#F6F1E6"), // sand
        ("#B4C7E7", "#EEF2FA"), // slate
        ("#E6A3A3", "#FBECEC")  // red
    };

    public static byte[] TemplateXlsx(ScoreTemplate template, GradingScheme scheme, string term)
    {
        var columns = ScoreColumns(template, scheme);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Scores");
        var first = IdentityHeaders.Length + 1;
        var lastCol = IdentityHeaders.Length + columns.Count;
        var lastRow = Math.Max(2, template.Students.Count + 1);

        for (var c = 0; c < IdentityHeaders.Length; c++) sheet.Cell(1, c + 1).Value = IdentityHeaders[c];
        for (var c = 0; c < columns.Count; c++) sheet.Cell(1, first + c).Value = columns[c].Header;

        var header = sheet.Range(1, 1, 1, lastCol);
        header.Style.Font.Bold = true;
        header.Style.Alignment.WrapText = true;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9EEF5");
        sheet.Row(1).Height = 48;

        // Each subject gets its own colour: a strong shade on its Assignment, CA and Exam headings
        // and a light tint of the same colour down its columns, with a thick line between subjects.
        var subjectIndex = template.Subjects.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        for (var c = 0; c < columns.Count; c++)
        {
            var (strong, light) = SubjectColours[subjectIndex[columns[c].SubjectId] % SubjectColours.Length];
            var cell = sheet.Cell(1, first + c);
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(strong);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (template.Students.Count > 0)
                sheet.Range(2, first + c, lastRow, first + c).Style.Fill.BackgroundColor = XLColor.FromHtml(light);
            if (c == 0 || columns[c].SubjectId != columns[c - 1].SubjectId)
                sheet.Range(1, first + c, lastRow, first + c).Style.Border.LeftBorder = XLBorderStyleValues.Thick;
        }
        if (columns.Count > 0)
            sheet.Range(1, lastCol, lastRow, lastCol).Style.Border.RightBorder = XLBorderStyleValues.Thick;
        sheet.Range(1, 1, 1, lastCol).Style.Border.BottomBorder = XLBorderStyleValues.Medium;

        var row = 2;
        foreach (var st in template.Students)
        {
            // IDs stay text so leading zeros survive.
            sheet.Cell(row, 1).SetValue(st.StudentId);
            sheet.Cell(row, 1).Style.NumberFormat.Format = "@";
            sheet.Cell(row, 2).Value = st.StudentName;
            sheet.Cell(row, 3).Value = st.Grade;
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = sheet.Cell(row, first + c);
                if (st.Scores.TryGetValue((columns[c].SubjectId, columns[c].Component), out var v)) cell.Value = v;
                if (!st.Takes.Contains(columns[c].SubjectId))
                {
                    cell.Style.Fill.PatternType = XLFillPatternValues.LightUp;
                    cell.Style.Fill.PatternColor = XLColor.FromHtml("#9E9E9E");
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E0E0");
                }
            }
            row++;
        }

        // Excel warns when a score is outside 0..the part's mark.
        for (var c = 0; c < columns.Count; c++)
        {
            var validation = sheet.Range(2, first + c, lastRow, first + c).CreateDataValidation();
            validation.Decimal.Between(0, columns[c].Weight);
            validation.IgnoreBlanks = true;
            validation.ErrorTitle = "Score out of range";
            validation.ErrorMessage = $"{PartName(columns[c].Component)} is out of {columns[c].Weight}. Enter a number from 0 to {columns[c].Weight}.";
        }

        sheet.SheetView.Freeze(1, 2);
        sheet.Columns(1, IdentityHeaders.Length).AdjustToContents(2, Math.Max(2, row - 1));
        for (var c = 1; c <= IdentityHeaders.Length; c++) sheet.Column(c).Width = Math.Max(sheet.Column(c).Width, 14);
        for (var c = first; c <= lastCol; c++) sheet.Column(c).Width = 14;

        var help = workbook.Worksheets.Add("How to fill");
        var lines = new[]
        {
            "How to fill this sheet",
            "",
            $"Term: {term}",
            $"Assignment is out of {scheme.AssignmentWeight}, CA is out of {scheme.CaWeight}, Exam is out of {scheme.ExamWeight} (total {scheme.Total}).",
            "There is one row per student and one column for each subject's Assignment, CA and Exam (e.g. \"Mathematics CA\").",
            "Type each student's scores in their row. Leave a cell empty to keep the score already in the app.",
            "Each subject has its own colour, so its Assignment, CA and Exam columns are easy to tell apart from the next subject's.",
            "Grey striped cells are subjects the student doesn't take; leave them empty.",
            "You can delete any columns you are not uploading (for example every Assignment column). The rest are still uploaded.",
            "Students are matched by Student ID first, then by name. Don't change the Student ID column or the subject names in the headings.",
            "You can add more CA columns for a subject (e.g. \"Mathematics 1st CA\", \"Mathematics 2nd CA\"); they are added together and must not exceed the CA total.",
            "Save the file and upload it on the Score Sheet > Upload page."
        };
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Column(1).Width = 120;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ---------- import ----------

    private enum Col { Ignore, Id, Name, FirstName, LastName, Subject, Assignment, Ca, Exam }

    // A score heading: an optional subject followed by the part, e.g. "Mathematics CA (out of 20)",
    // "Eng 2nd CA", "Biology Exam Score", or just "Exam".
    private static readonly Regex ScoreHeading = new(
        @"^(?<subject>.*?)[\s\-:/.]*\b(?<part>assignments?|homeworks?|continuous\s*assessments?|" +
        @"(?:(?:\d+\s*(?:st|nd|rd|th)?|first|second|third|fourth)\s*)?(?:ca\s*tests?|ca|tests?)\s*\d*|exams?|examinations?)" +
        @"(?:\s*(?:scores?|marks?))?$", RegexOptions.IgnoreCase);

    private static (string Subject, string Component)? ParseScoreHeading(string header)
    {
        var h = header.Replace('_', ' ').Trim();
        // Drop trailing notes such as "(out of 20)".
        while (Regex.IsMatch(h, @"\([^()]*\)\s*$")) h = Regex.Replace(h, @"\s*\([^()]*\)\s*$", "");
        var m = ScoreHeading.Match(h);
        if (m.Success)
        {
            var part = m.Groups["part"].Value.ToLowerInvariant();
            var component = part.StartsWith("assign") || part.StartsWith("homework") ? ScoreComponents.Assignment
                : part.StartsWith("exam") ? ScoreComponents.Exam : ScoreComponents.Ca;
            return (m.Groups["subject"].Value.Trim(), component);
        }
        return Classify(header) switch
        {
            Col.Assignment => (string.Empty, ScoreComponents.Assignment),
            Col.Ca => (string.Empty, ScoreComponents.Ca),
            Col.Exam => (string.Empty, ScoreComponents.Exam),
            _ => null
        };
    }

    private static bool IsIdentity(Col c) => c is Col.Id or Col.Name or Col.FirstName or Col.LastName or Col.Subject;

    private static string SubjectKey(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

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
        var headerIndex = rows.FindIndex(r => r.Any(c => !IsIdentity(Classify(c)) && ParseScoreHeading(c) != null));
        if (headerIndex < 0)
        {
            result.Errors.Add("No score columns found. The first row needs headings such as Student ID, Student Name and \"Mathematics Assignment\", \"Mathematics CA\", \"Mathematics Exam\".");
            return result;
        }

        var headerRow = rows[headerIndex];
        var cols = headerRow.Select(Classify).ToList();
        int IndexOf(Col c) => cols.IndexOf(c);
        var idCol = IndexOf(Col.Id);
        var nameCol = IndexOf(Col.Name);
        var firstCol = IndexOf(Col.FirstName);
        var lastCol = IndexOf(Col.LastName);
        var subjectCol = IndexOf(Col.Subject);

        var subjects = await _context.Subjects.Where(s => scope.SubjectIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Code }).ToListAsync();
        var allSubjectNames = await _context.Subjects.Select(s => new { s.Name, s.Code }).ToListAsync();
        var subjectNames = subjects.ToDictionary(s => s.Id, s => s.Name.Trim());

        // Finds a subject from a heading or cell: by name, code or "name code"; else a unique name starting with it ("Eng").
        (int? Id, string? Problem) FindSubject(string text)
        {
            var key = SubjectKey(text);
            var norm = Norm(text);
            var match = subjects.Where(s => SubjectKey(s.Name) == key || SubjectKey(s.Code) == key || SubjectKey(s.Name + s.Code) == key
                                            || (norm.Length > 0 && (Norm(s.Name) == norm || Norm(s.Code) == norm))).ToList();
            if (match.Count == 0 && key.Length >= 3)
                match = subjects.Where(s => SubjectKey(s.Name).StartsWith(key)).ToList();
            if (match.Count == 1) return (match[0].Id, null);
            if (match.Count > 1) return (null, $"more than one subject matches \"{text}\". Use the subject code.");
            return (null, !scope.IsAdmin && allSubjectNames.Any(s => SubjectKey(s.Name) == key || SubjectKey(s.Code) == key)
                ? $"\"{text}\" is not one of your subjects."
                : $"subject \"{text}\" was not found.");
        }

        // Score columns: plain ones ("CA") use the row's subject; others name the subject ("Mathematics CA").
        var plainCols = new Dictionary<string, List<int>>();
        var subjectCols = new Dictionary<int, Dictionary<string, List<int>>>();
        var skipped = new List<string>();
        for (var i = 0; i < headerRow.Count; i++)
        {
            if (IsIdentity(cols[i]) || ParseScoreHeading(headerRow[i]) is not { } heading) continue;
            var title = headerRow[i].Trim();
            if (scheme.WeightOf(heading.Component) == 0)
            {
                skipped.Add($"\"{title}\": {PartName(heading.Component)} is worth 0 marks in the grading setup.");
                continue;
            }
            if (heading.Subject.Length == 0)
            {
                if (!plainCols.TryGetValue(heading.Component, out var list)) plainCols[heading.Component] = list = new List<int>();
                list.Add(i);
                continue;
            }
            var (sid, problem) = FindSubject(heading.Subject);
            if (sid == null)
            {
                skipped.Add($"\"{title}\": {problem}");
                continue;
            }
            if (!subjectCols.TryGetValue(sid.Value, out var parts)) subjectCols[sid.Value] = parts = new Dictionary<string, List<int>>();
            if (!parts.TryGetValue(heading.Component, out var idx)) parts[heading.Component] = idx = new List<int>();
            idx.Add(i);
        }

        var plainUsable = plainCols.Count > 0 && (subjectCol >= 0 || defaultSubjectId != null);
        if (plainCols.Count > 0 && !plainUsable && subjectCols.Count > 0)
        {
            skipped.Add($"{string.Join(", ", plainCols.SelectMany(p => p.Value).Select(i => $"\"{headerRow[i].Trim()}\""))}: the heading doesn't say which subject. Name it like \"Mathematics CA\", add a Subject column or choose the subject on the upload page.");
            plainCols.Clear();
        }
        result.ColumnWarnings.AddRange(skipped);

        string Parts(Dictionary<string, List<int>> parts) => string.Join(", ",
            new[] { ScoreComponents.Assignment, ScoreComponents.Ca, ScoreComponents.Exam }.Where(parts.ContainsKey)
                .Select(c => c == ScoreComponents.Ca && parts[c].Count > 1 ? $"CA ({parts[c].Count} columns added together)" : PartName(c)));
        if (plainCols.Count > 0) result.ColumnsFound.Add(Parts(plainCols));
        foreach (var (sid, parts) in subjectCols.OrderBy(x => subjectNames[x.Key]))
            result.ColumnsFound.Add($"{subjectNames[sid]} ({Parts(parts)})");

        if (idCol < 0 && nameCol < 0 && (firstCol < 0 || lastCol < 0))
            result.Errors.Add("Add a \"Student ID\" or \"Student Name\" column so each row can be matched to a student.");
        if (plainCols.Count > 0 && !plainUsable)
            result.Errors.Add("Add a \"Subject\" column, or choose the subject on the upload page, or name the score columns by subject (e.g. \"Mathematics CA\").");
        else if (plainCols.Count == 0 && subjectCols.Count == 0)
            result.Errors.Add("None of the score columns could be used:");
        var dataRows = rows.Skip(headerIndex + 1).Select((r, i) => (Cells: r, Line: headerIndex + 2 + i))
            .Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (dataRows.Count == 0) result.Errors.Add("The file has no rows below the heading row.");
        if (dataRows.Count > MaxRows) result.Errors.Add($"The file has {dataRows.Count} rows. Upload at most {MaxRows} at a time.");
        if (result.Errors.Any()) return result;

        // Students the user may record scores for.
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

        var recorded = await _results.LoadRecordedAsync(students.Select(s => s.Id).ToList(), term);
        var seen = new Dictionary<(int, int), int>();

        foreach (var (cells, line) in dataRows)
        {
            string Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : string.Empty;
            bool AnyFilled(IEnumerable<List<int>> lists) => lists.SelectMany(l => l).Any(i => Cell(i).Length > 0);

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
                if (!AnyFilled(plainCols.Values.Concat(subjectCols.Values.SelectMany(p => p.Values)))) continue; // empty row
                result.RowErrors.Add(id.Length == 0 && name.Length == 0
                    ? $"Row {line}: no Student ID or name."
                    : $"Row {line}: student \"{label}\" was not found{(scope.IsAdmin ? "" : " in your grades")}.");
                continue;
            }

            // Which columns hold which subject's scores on this row.
            var groups = subjectCols.ToDictionary(x => x.Key, x => x.Value.ToDictionary(p => p.Key, p => p.Value.ToList()));
            if (plainUsable && AnyFilled(plainCols.Values))
            {
                // Plain columns: the Subject column, or the subject chosen on the page.
                int? subjectId = defaultSubjectId;
                var subjectText = Cell(subjectCol);
                string? problem = null;
                if (subjectText.Length > 0)
                {
                    (subjectId, problem) = FindSubject(subjectText);
                }
                else if (defaultSubjectId == null) problem = $"no subject for {label}.";
                else if (!scope.SubjectIds.Contains(defaultSubjectId.Value)) problem = "you can't record scores for that subject.";

                if (problem != null || subjectId == null)
                {
                    result.RowErrors.Add($"Row {line}: {problem ?? "no subject."}");
                    if (groups.Count == 0) continue;
                }
                else
                {
                    if (!groups.TryGetValue(subjectId.Value, out var parts)) groups[subjectId.Value] = parts = new Dictionary<string, List<int>>();
                    foreach (var (component, list) in plainCols)
                    {
                        if (!parts.TryGetValue(component, out var idx)) parts[component] = idx = new List<int>();
                        idx.AddRange(list);
                    }
                }
            }

            var recordedOnRow = false;
            foreach (var (subjectId, parts) in groups.OrderBy(g => subjectNames[g.Key]))
            {
                var where = groups.Count > 1 || subjectCols.Count > 0 ? $" {subjectNames[subjectId]}" : string.Empty;

                // Scores: blank cells keep what's in the app; several columns for one part are added together.
                var values = new Dictionary<string, decimal>();
                var problems = new List<string>();
                foreach (var (component, list) in parts)
                {
                    var partLabel = PartName(component);
                    var filled = list.Select(Cell).Where(v => v.Length > 0).ToList();
                    if (filled.Count == 0) continue;

                    decimal sum = 0;
                    var ok = true;
                    foreach (var raw in filled)
                    {
                        if (!TryScore(raw, out var v) || v < 0) { problems.Add($"{partLabel} \"{raw}\" is not a valid score"); ok = false; break; }
                        sum += v;
                    }
                    if (!ok) continue;

                    var weight = scheme.WeightOf(component);
                    if (sum > weight) { problems.Add($"{partLabel} {sum.ToString("0.##", CultureInfo.InvariantCulture)} is more than {weight}"); continue; }
                    values[component] = Math.Round(sum, 2);
                }
                if (problems.Any())
                {
                    result.RowErrors.Add($"Row {line} ({label}){where}: {string.Join("; ", problems)}. {(where.Length > 0 ? $"{subjectNames[subjectId]} skipped" : "Row skipped")}.");
                    continue;
                }
                if (values.Count == 0) continue; // nothing to record for this subject

                if (seen.TryGetValue((studentId.Value, subjectId), out var firstLine))
                {
                    result.RowErrors.Add($"Row {line}: {label} already appears for {subjectNames[subjectId]} on row {firstLine}; this row's {subjectNames[subjectId]} scores were skipped.");
                    continue;
                }
                seen[(studentId.Value, subjectId)] = line;

                foreach (var (component, value) in values)
                {
                    if (_results.SetRecorded(recorded, studentId.Value, subjectId, term, component, value, scheme, userId))
                        result.ScoresChanged++;
                }
                recordedOnRow = true;
            }
            if (recordedOnRow) result.RowsImported++;
        }

        await _context.SaveChangesAsync();
        return result;
    }
}
