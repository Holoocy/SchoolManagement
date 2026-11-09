namespace SchoolManagement.Services;

// Exam start times are entered and shown as the school's local wall-clock time, while
// the server runs on UTC. This converts "now" into that time zone so the two compare
// correctly. Set "SchoolTimeZone" (an IANA id such as "Africa/Lagos") in configuration.
public interface ISchoolClock
{
    DateTime Now { get; }
}

public class SchoolClock : ISchoolClock
{
    private readonly TimeZoneInfo _timeZone;

    public SchoolClock(IConfiguration configuration, ILogger<SchoolClock> logger)
    {
        var id = configuration["SchoolTimeZone"];
        if (string.IsNullOrWhiteSpace(id)) id = "Africa/Lagos";

        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Time zone {TimeZone} was not found; using UTC for exam times.", id);
            _timeZone = TimeZoneInfo.Utc;
        }
    }

    public DateTime Now => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone), DateTimeKind.Unspecified);
}
