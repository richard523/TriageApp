namespace TriageApp.Services;

/// <summary>
/// Startup facts about the running configuration, surfaced to the UI
/// (demo-mode banners, sign-in visibility, etc.).
/// </summary>
public class AppInfo
{
    public bool AuthEnabled { get; set; }
    public bool DbAvailable { get; set; }
    public bool JiraConfigured { get; set; }
    public string DbMode => DbAvailable ? "SQL Server" : "In-memory (demo mode)";
}
