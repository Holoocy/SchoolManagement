namespace SchoolManagement.Services;

public interface ITenantProvider
{
    int SchoolId { get; }
    string? Slug { get; }
    string? SchoolName { get; }
    string? LogoPath { get; }
    string ThemeColor { get; }
    bool IsResolved { get; }
    void SetSchool(int id, string slug, string name, string? logoPath, string themeColor);
}

public class TenantProvider : ITenantProvider
{
    public int SchoolId { get; private set; }
    public string? Slug { get; private set; }
    public string? SchoolName { get; private set; }
    public string? LogoPath { get; private set; }
    public string ThemeColor { get; private set; } = "#2563eb";
    public bool IsResolved => SchoolId != 0;

    public void SetSchool(int id, string slug, string name, string? logoPath, string themeColor)
    {
        SchoolId = id;
        Slug = slug;
        SchoolName = name;
        LogoPath = logoPath;
        ThemeColor = string.IsNullOrWhiteSpace(themeColor) ? "#2563eb" : themeColor;
    }
}
