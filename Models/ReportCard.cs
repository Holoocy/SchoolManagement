using System.Globalization;
using System.Text.Json;

namespace SchoolManagement.Models;

public static class ReportCardStatus
{
    public const string Draft = "Draft";          // teacher is preparing it
    public const string Pending = "Pending";      // sent to the Admin for approval
    public const string Approved = "Approved";    // approved, not sent yet
    public const string Rejected = "Rejected";    // sent back to the teacher with a note
    public const string Sent = "Sent";            // sent to students and parents

    public static string Label(string status) => status switch
    {
        Pending => "Waiting for approval",
        Approved => "Approved – ready to send",
        Rejected => "Sent back",
        Sent => "Sent to students & parents",
        _ => "Draft"
    };

    public static string Badge(string status) => status switch
    {
        Pending => "status-pending",
        Approved => "status-approved",
        Rejected => "status-rejected",
        Sent => "status-completed",
        _ => "status-draft"
    };
}

// The report cards of one grade for one term, and where they are in approval.
public class ReportCardBatch : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int ClassId { get; set; }
    public Class? Class { get; set; }

    public string Term { get; set; } = string.Empty;
    public string Status { get; set; } = ReportCardStatus.Draft;

    public int? SubmittedByUserId { get; set; }
    public string? SubmittedByName { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmitNote { get; set; }

    public int? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public int? SentByUserId { get; set; }
    public string? SentByName { get; set; }
    public DateTime? SentAt { get; set; }
    public int TimesSent { get; set; }

    public ICollection<ReportCard> Cards { get; set; } = new List<ReportCard>();

    public bool IsEditable => Status is ReportCardStatus.Draft or ReportCardStatus.Rejected;
}

// One student's report card in a batch.
public class ReportCard : ITenantEntity
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int BatchId { get; set; }
    public ReportCardBatch? Batch { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public string? TeacherRemark { get; set; }

    // Frozen when the batch is submitted: what the Admin approves.
    public string? DataJson { get; set; }

    // What the student and parents see: the approved card as last sent.
    public string? SentDataJson { get; set; }
    public DateTime? SentAt { get; set; }

    public ReportCardData? Data => ReportCardData.FromJson(DataJson);
    public ReportCardData? SentData => ReportCardData.FromJson(SentDataJson);
}

// Everything printed on a report card, stored as JSON so it doesn't change after approval.
public class ReportCardData
{
    public string SchoolName { get; set; } = string.Empty;
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AdmissionNumber { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;

    public int AssignmentWeight { get; set; }
    public int CaWeight { get; set; }
    public int ExamWeight { get; set; }
    public int Decimals { get; set; }
    public int Total => AssignmentWeight + CaWeight + ExamWeight;

    public List<ReportCardSubject> Subjects { get; set; } = new();
    public decimal? Average { get; set; }
    public int? Position { get; set; }
    public int ClassSize { get; set; }
    public int PresentDays { get; set; }
    public int AttendanceDays { get; set; }

    public string? TeacherRemark { get; set; }
    public string? PreparedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? IssuedAt { get; set; }

    public string Format(decimal? value) => value == null
        ? "—"
        : value.Value.ToString(Decimals == 0 ? "0" : "0." + new string('#', Decimals), CultureInfo.InvariantCulture);

    public int SubjectsWithScores => Subjects.Count(s => s.Total != null);

    // The scores part, used to tell whether results changed since the card was frozen.
    public string ScoresKey() => JsonSerializer.Serialize(new { Subjects, Average, Position, ClassSize, AssignmentWeight, CaWeight, ExamWeight, Decimals });

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public string ToJson() => JsonSerializer.Serialize(this);
    public static ReportCardData? FromJson(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<ReportCardData>(json, Options);
}

public class ReportCardSubject
{
    public string Name { get; set; } = string.Empty;
    public decimal? Assignment { get; set; }
    public decimal? Ca { get; set; }
    public decimal? Exam { get; set; }
    public decimal? Total { get; set; }
}

// ---------- view models ----------

public class ReportCardsIndexViewModel
{
    public bool IsAdmin { get; set; }
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public string CurrentTerm { get; set; } = string.Empty;
    public List<ReportCardGradeLine> Grades { get; set; } = new();
}

public class ReportCardGradeLine
{
    public Class Class { get; set; } = null!;
    public int Students { get; set; }
    public ReportCardBatch? Batch { get; set; }
    public string Status => Batch?.Status ?? ReportCardStatus.Draft;
}

public class ReportCardGradeViewModel
{
    public bool IsAdmin { get; set; }
    public Class Class { get; set; } = null!;
    public string Term { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public ReportCardBatch? Batch { get; set; }
    public string Status => Batch?.Status ?? ReportCardStatus.Draft;
    public bool IsEditable => Batch == null || Batch.IsEditable;
    public List<ReportCardGradeRow> Rows { get; set; } = new();
    public bool ScoresChangedSinceSubmit { get; set; }
    public int StudentsWithoutAccounts { get; set; }
}

public class ReportCardGradeRow
{
    public int StudentId { get; set; }
    public ReportCardData Data { get; set; } = new();
    public string? Remark { get; set; }
    public bool HasLogin { get; set; }
    public int Parents { get; set; }
    public DateTime? SentAt { get; set; }
}

public class ReportCardPageViewModel
{
    public ReportCardData Data { get; set; } = new();
    public string? Notice { get; set; }      // e.g. "Preview – not approved yet"
    public string BackText { get; set; } = "Back";
    public string? BackUrl { get; set; }
}
