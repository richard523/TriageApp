using Microsoft.Data.SqlClient;
using TriageApp.Data;

namespace TriageApp.Services;

// ---------------------------------------------------------------------------
// Triage ticket store
// ---------------------------------------------------------------------------

public interface ITriageStore
{
    Task<Ticket> AddAsync(Ticket ticket);
    Task UpdateJiraAsync(int id, string? jiraKey, string? jiraUrl, string? jiraError);
    Task<IReadOnlyList<Ticket>> GetAllAsync();
    Task<bool> DecideAsync(int id, bool approve, string decidedBy);
}

public class DbTriageStore(AppDbContext db) : ITriageStore
{
    public async Task<Ticket> AddAsync(Ticket ticket)
    {
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    public async Task UpdateJiraAsync(int id, string? jiraKey, string? jiraUrl, string? jiraError)
    {
        var ticket = await db.Tickets.FindAsync(id);
        if (ticket is null) return;
        ticket.JiraKey = jiraKey;
        ticket.JiraUrl = jiraUrl;
        ticket.JiraError = jiraError;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Ticket>> GetAllAsync() =>
        db.Tickets.OrderByDescending(t => t.CreatedAtUtc).ToList();

    public async Task<bool> DecideAsync(int id, bool approve, string decidedBy)
    {
        var ticket = await db.Tickets.FindAsync(id);
        if (ticket is null || ticket.Status != TicketStatus.Submitted) return false;
        ticket.Status = approve ? TicketStatus.Approved : TicketStatus.Rejected;
        ticket.DecidedBy = decidedBy;
        ticket.DecidedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }
}

public class InMemoryTriageStore : ITriageStore
{
    private readonly List<Ticket> _tickets = [];
    private int _nextId = 1;

    public Task<Ticket> AddAsync(Ticket ticket)
    {
        ticket.Id = _nextId++;
        lock (_tickets) { _tickets.Add(ticket); }
        return Task.FromResult(ticket);
    }

    public Task UpdateJiraAsync(int id, string? jiraKey, string? jiraUrl, string? jiraError)
    {
        lock (_tickets)
        {
            var ticket = _tickets.FirstOrDefault(t => t.Id == id);
            if (ticket is not null)
            {
                ticket.JiraKey = jiraKey;
                ticket.JiraUrl = jiraUrl;
                ticket.JiraError = jiraError;
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Ticket>> GetAllAsync()
    {
        lock (_tickets) { return Task.FromResult<IReadOnlyList<Ticket>>(_tickets.OrderByDescending(t => t.CreatedAtUtc).ToList()); }
    }

    public Task<bool> DecideAsync(int id, bool approve, string decidedBy)
    {
        lock (_tickets)
        {
            var ticket = _tickets.FirstOrDefault(t => t.Id == id);
            if (ticket is null || ticket.Status != TicketStatus.Submitted) return Task.FromResult(false);
            ticket.Status = approve ? TicketStatus.Approved : TicketStatus.Rejected;
            ticket.DecidedBy = decidedBy;
            ticket.DecidedAtUtc = DateTime.UtcNow;
            return Task.FromResult(true);
        }
    }
}

// ---------------------------------------------------------------------------
// Quiz progress store
// ---------------------------------------------------------------------------

public interface IQuizStore
{
    Task<IReadOnlyList<QuizProgress>> GetAsync(string userId);
    Task SaveAsync(QuizProgress progress);
    Task ResetAsync(string userId);
}

public class DbQuizStore(AppDbContext db) : IQuizStore
{
    public async Task<IReadOnlyList<QuizProgress>> GetAsync(string userId) =>
        db.QuizProgress.Where(p => p.UserId == userId).ToList();

    public async Task SaveAsync(QuizProgress progress)
    {
        var existing = await db.QuizProgress.FindAsync(progress.Id);
        if (existing is null)
        {
            db.QuizProgress.Add(progress);
        }
        else
        {
            existing.CorrectStreak = progress.CorrectStreak;
            existing.IsMastered = progress.IsMastered;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    public async Task ResetAsync(string userId)
    {
        db.QuizProgress.RemoveRange(db.QuizProgress.Where(p => p.UserId == userId));
        await db.SaveChangesAsync();
    }
}

public class InMemoryQuizStore : IQuizStore
{
    private readonly Dictionary<(string UserId, string QuestionId), QuizProgress> _progress = new();

    public Task<IReadOnlyList<QuizProgress>> GetAsync(string userId)
    {
        lock (_progress)
        {
            return Task.FromResult<IReadOnlyList<QuizProgress>>(
                _progress.Values.Where(p => p.UserId == userId).ToList());
        }
    }

    public Task SaveAsync(QuizProgress progress)
    {
        lock (_progress)
        {
            var key = (progress.UserId, progress.QuestionId);
            if (_progress.TryGetValue(key, out var existing))
            {
                existing.CorrectStreak = progress.CorrectStreak;
                existing.IsMastered = progress.IsMastered;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _progress[key] = progress;
            }
        }
        return Task.CompletedTask;
    }

    public Task ResetAsync(string userId)
    {
        lock (_progress)
        {
            foreach (var key in _progress.Keys.Where(k => k.UserId == userId).ToList())
                _progress.Remove(key);
        }
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// SQL Server connectivity check + schema introspection
// ---------------------------------------------------------------------------

public static class SqlServerChecker
{
    public static async Task<bool> TryConnectAsync(string connectionString)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await conn.OpenAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public record SqlColumn(string Name, string Type, bool Nullable, bool IsPrimaryKey, string? ForeignKeyTarget);
public record SqlTable(string Name, IReadOnlyList<SqlColumn> Columns);

public class SchemaService
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
        ?? "Server=localhost\\SQLEXPRESS;Database=TriageApp;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    /// <summary>Returns null when SQL Server is unreachable.</summary>
    public async Task<IReadOnlyList<SqlTable>?> GetTablesAsync()
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var columns = new Dictionary<string, List<SqlColumn>>(StringComparer.OrdinalIgnoreCase);
            var pk = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var fk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new SqlCommand(
                "SELECT tc.TABLE_NAME, ku.COLUMN_NAME " +
                "FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc " +
                "JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku ON ku.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND ku.TABLE_SCHEMA = tc.TABLE_SCHEMA " +
                "WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_SCHEMA = 'dbo'", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(0);
                    if (!pk.TryGetValue(table, out var set)) pk[table] = set = [];
                    set.Add(reader.GetString(1));
                }
            }

            using (var cmd = new SqlCommand(
                "SELECT fk.name, tp.name, cp.name, tr.name, cr.name " +
                "FROM sys.foreign_keys fk " +
                "JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id " +
                "JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id " +
                "JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id " +
                "JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id " +
                "JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(1);
                    var column = reader.GetString(2);
                    fk[$"{table}.{column}"] = $"{reader.GetString(3)}.{reader.GetString(4)}";
                }
            }

            using (var cmd = new SqlCommand(
                "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE " +
                "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' " +
                "ORDER BY TABLE_NAME, ORDINAL_POSITION", conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(0);
                    var column = reader.GetString(1);
                    if (!columns.TryGetValue(table, out var list)) columns[table] = list = [];
                    list.Add(new SqlColumn(
                        column,
                        reader.GetString(2),
                        reader.GetString(3) == "YES",
                        pk.TryGetValue(table, out var keys) && keys.Contains(column),
                        fk.TryGetValue($"{table}.{column}", out var target) ? target : null));
                }
            }

            return columns
                .Select(kv => new SqlTable(kv.Key, kv.Value))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return null;
        }
    }
}
