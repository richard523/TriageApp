using System.Text;

namespace TriageApp.Services;

/// <summary>
/// Minimal .env loader. Reads KEY=VALUE lines from a .env file and exposes them as
/// process environment variables (without overwriting variables that already exist).
/// Looks for .env in the content root, the working directory, and their parents.
/// </summary>
public static class EnvLoader
{
    public static void Load()
    {
        var candidates = new List<string>();
        try { candidates.Add(Directory.GetCurrentDirectory()); } catch { /* ignore */ }
        candidates.Add(AppContext.BaseDirectory);

        foreach (var start in candidates)
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var envPath = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envPath))
                {
                    LoadFile(envPath);
                    return;
                }
                dir = dir.Parent;
            }
        }
    }

    private static void LoadFile(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            // Allow optional "export " prefix.
            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                line = line["export ".Length..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = Unquote(line[(eq + 1)..].Trim());

            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
                return value[1..^1];
        }
        return value;
    }
}
