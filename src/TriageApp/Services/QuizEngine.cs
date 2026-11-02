using System.Text.Json;

namespace TriageApp.Services;

public record QuizSource(string Title, string Author, string Edition, string Publisher);
public record QuizQuestion(string Id, string Topic, string Prompt, IReadOnlyList<string> Options, int AnswerIndex, string Explanation, string SourceChapter);
public record QuizData(QuizSource Source, int MasteryThreshold, IReadOnlyList<QuizQuestion> Questions);

/// <summary>
/// Loads the quiz bank from quiz.json (copied to the output directory).
/// </summary>
public class QuizEngine
{
    public QuizData Data { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public QuizEngine()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "quiz.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("quiz.json not found next to the application.", path);

        Data = JsonSerializer.Deserialize<QuizData>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("quiz.json could not be parsed.");

        // .env override wins.
        if (int.TryParse(Environment.GetEnvironmentVariable("QUIZ_MASTERY_THRESHOLD"), out var threshold) && threshold > 0)
            Data = Data with { MasteryThreshold = threshold };
    }
}
