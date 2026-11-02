using System.Text.RegularExpressions;

namespace TriageApp.Services;

public record Classification(
    string Layer,
    string LayerSlug,
    double Confidence,
    IReadOnlyList<string> MatchedKeywords,
    string Rationale);

/// <summary>
/// Rule-based classifier that maps a feature request to the architectural layer
/// it most likely lands in, based on keyword evidence.
/// </summary>
public class ArchitectureClassifier
{
    private static readonly (string Layer, string Slug, string[] Keywords)[] Layers =
    [
        ("Presentation / UI", "arch-presentation",
        [
            "ui", "button", "page", "screen", "form", "css", "layout", "menu", "dashboard",
            "chart", "display", "dialog", "modal", "tooltip", "responsive", "frontend",
            "dropdown", "tab", "widget", "icon", "animation", "accessibility"
        ]),
        ("Application / API", "arch-application",
        [
            "api", "endpoint", "controller", "route", "request", "response", "middleware",
            "auth", "login", "token", "rest", "graphql", "rate limit", "throttle",
            "versioning", "swagger", "openapi"
        ]),
        ("Domain / Business Logic", "arch-domain",
        [
            "business", "rule", "rules", "validation", "workflow", "calculation", "pricing",
            "policy", "logic", "eligibility", "triage", "approval", "discount", "tax",
            "scenario", "requirement", "feature", "report"
        ]),
        ("Data Access", "arch-data",
        [
            "database", "table", "schema", "query", "sql", "entity", "migration",
            "repository", "crud", "index", "stored procedure", "column", "record",
            "seed data", "backup", "transaction"
        ]),
        ("Infrastructure / Integration", "arch-infrastructure",
        [
            "queue", "message", "logging", "deploy", "deployment", "ci", "cd", "pipeline",
            "cache", "caching", "storage", "email", "notification", "jira", "integration",
            "monitoring", "telemetry", "scaling", "infrastructure", "hosting", "certificate",
            "webhook", "scheduler", "job", "service bus", "kafka", "redis"
        ])
    ];

    public IReadOnlyList<string> LayerNames => Layers.Select(l => l.Layer).ToArray();

    public Classification Classify(string text)
    {
        var lower = " " + text.ToLowerInvariant().Replace('\n', ' ') + " ";

        var bestLayer = Layers[0];
        var bestKeywords = new List<string>();
        var totalHits = 0;

        foreach (var layer in Layers)
        {
            var hits = new List<string>();
            foreach (var kw in layer.Keywords)
            {
                if (Regex.IsMatch(lower, $@"(?<![a-z0-9]){Regex.Escape(kw)}(?![a-z0-9])"))
                    hits.Add(kw);
            }

            totalHits += hits.Count;
            if (hits.Count > bestKeywords.Count)
            {
                bestLayer = layer;
                bestKeywords = hits;
            }
        }

        if (bestKeywords.Count == 0)
        {
            return new Classification(
                "Application / API",
                "arch-application",
                0.0,
                [],
                "No architectural keywords detected — defaulted to the Application/API layer for human review.");
        }

        var confidence = Math.Min(1.0, Math.Round(0.2 + 0.25 * bestKeywords.Count, 2));

        return new Classification(
            bestLayer.Layer,
            bestLayer.Slug,
            confidence,
            bestKeywords,
            $"Matched {bestKeywords.Count} layer keyword(s): {string.Join(", ", bestKeywords)}.");
    }
}
