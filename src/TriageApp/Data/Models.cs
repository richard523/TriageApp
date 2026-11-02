namespace TriageApp.Data;

public enum TicketStatus
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2
}

public class Ticket
{
    public int Id { get; set; }
    public string Summary { get; set; } = "";
    public string RawText { get; set; } = "";

    // Architecture classification
    public string Layer { get; set; } = "";
    public double Confidence { get; set; }
    public string MatchedKeywords { get; set; } = "";

    // Jira integration
    public string? JiraKey { get; set; }
    public string? JiraUrl { get; set; }
    public string? JiraError { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.Submitted;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}

public class QuizProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public int CorrectStreak { get; set; }
    public bool IsMastered { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
