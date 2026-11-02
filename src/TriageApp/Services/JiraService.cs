using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TriageApp.Data;

namespace TriageApp.Services;

public record JiraCreateResult(bool Success, string? Key, string? Url, string? Error);

public interface IJiraService
{
    bool IsConfigured { get; }
    Task<JiraCreateResult> CreateTicketAsync(Ticket ticket, Classification classification);
    Task<string?> TransitionAsync(string issueKey, string transitionName);
}

/// <summary>
/// Jira Cloud REST API v3 client. All settings come from environment variables
/// (loaded from .env): JIRA_BASE_URL, JIRA_USER_EMAIL, JIRA_API_TOKEN,
/// JIRA_PROJECT_KEY, JIRA_ISSUE_TYPE.
/// </summary>
public class JiraService(IHttpClientFactory httpClientFactory) : IJiraService
{
    private readonly string? _baseUrl = Environment.GetEnvironmentVariable("JIRA_BASE_URL")?.TrimEnd('/');
    private readonly string? _email = Environment.GetEnvironmentVariable("JIRA_USER_EMAIL");
    private readonly string? _token = Environment.GetEnvironmentVariable("JIRA_API_TOKEN");
    private readonly string? _projectKey = Environment.GetEnvironmentVariable("JIRA_PROJECT_KEY");
    private readonly string _issueType = Environment.GetEnvironmentVariable("JIRA_ISSUE_TYPE") ?? "Task";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_baseUrl) &&
        !string.IsNullOrWhiteSpace(_email) &&
        !string.IsNullOrWhiteSpace(_token) &&
        !string.IsNullOrWhiteSpace(_projectKey);

    public async Task<JiraCreateResult> CreateTicketAsync(Ticket ticket, Classification classification)
    {
        if (!IsConfigured)
            return new JiraCreateResult(false, null, null,
                "Jira is not configured (set JIRA_* variables in .env) — the ticket was stored locally only.");

        try
        {
            var http = httpClientFactory.CreateClient();
            SetAuth(http);

            var payload = new
            {
                fields = new
                {
                    project = new { key = _projectKey },
                    summary = ticket.Summary,
                    description = BuildDescription(ticket, classification),
                    issuetype = new { name = _issueType },
                    labels = new[] { classification.LayerSlug, "triage-app" }
                }
            };

            using var response = await http.PostAsJsonAsync($"{_baseUrl}/rest/api/3/issue", payload);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return new JiraCreateResult(false, null, null, $"Jira API returned {(int)response.StatusCode}: {Trim(body)}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var key = doc.RootElement.GetProperty("key").GetString();
            var url = $"{_baseUrl}/browse/{key}";
            return new JiraCreateResult(true, key, url, null);
        }
        catch (Exception ex)
        {
            return new JiraCreateResult(false, null, null, $"Could not reach Jira: {ex.Message}");
        }
    }

    public async Task<string?> TransitionAsync(string issueKey, string transitionName)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(transitionName))
            return null;

        try
        {
            var http = httpClientFactory.CreateClient();
            SetAuth(http);

            var listResponse = await http.GetFromJsonAsync<JsonElement>($"{_baseUrl}/rest/api/3/issue/{issueKey}/transitions");
            foreach (var t in listResponse.GetProperty("transitions").EnumerateArray())
            {
                var name = t.GetProperty("name").GetString();
                var toName = t.TryGetProperty("to", out var to) && to.TryGetProperty("name", out var toN) ? toN.GetString() : null;
                if (string.Equals(name, transitionName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(toName, transitionName, StringComparison.OrdinalIgnoreCase))
                {
                    var id = t.GetProperty("id").GetString();
                    using var post = await http.PostAsJsonAsync(
                        $"{_baseUrl}/rest/api/3/issue/{issueKey}/transitions",
                        new { transition = new { id } });
                    return post.IsSuccessStatusCode
                        ? null
                        : $"Jira transition failed with {(int)post.StatusCode}";
                }
            }
            return $"No Jira transition named '{transitionName}' is available for {issueKey}.";
        }
        catch (Exception ex)
        {
            return $"Jira transition error: {ex.Message}";
        }
    }

    private void SetAuth(HttpClient http)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_email}:{_token}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>Atlassian Document Format (ADF) description.</summary>
    private static object BuildDescription(Ticket ticket, Classification c)
    {
        string ConfidenceText(double v) => v > 0 ? $"{(int)(v * 100)}%" : "n/a";
        string keywords = c.MatchedKeywords.Count > 0 ? string.Join(", ", c.MatchedKeywords) : "none";

        return new
        {
            type = "doc",
            version = 1,
            content = new object[]
            {
                Para("Submitted via TriageApp automatic ticket triage."),
                Para($"Architectural layer: {c.Layer} (classifier confidence: {ConfidenceText(c.Confidence)})."),
                Para($"Matched keywords: {keywords}."),
                Para("Original request:"),
                Para(ticket.RawText)
            }
        };
    }

    private static object Para(string text) => new
    {
        type = "paragraph",
        content = new object[] { new { type = "text", text } }
    };

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
