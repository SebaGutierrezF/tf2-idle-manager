using System.Text.Json;

namespace TF2IdleManager;

// New sessions only: older drops contain SessionId but cannot prove full session duration.
// This ledger never fabricates historical start/end times from item timestamps.
public sealed class ManagedSessionRecord
{
    public string SteamId64 { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Server { get; set; } = "";
    public string SelectedClass { get; set; } = "";
    public double EligibleSeconds { get; set; }
    public double ApiOutageSeconds { get; set; }
    public string State { get; set; } = "Running"; // Running, Completed, Interrupted
}

public static class SessionHistoryStore
{
    private static readonly object Sync = new();
    private static string PathForActiveAccount() => AccountDataContext.GetActiveFilePath("sessions.json");

    public static IReadOnlyList<ManagedSessionRecord> ReadAll()
    {
        if (!AccountDataContext.HasActiveAccount) return Array.Empty<ManagedSessionRecord>();
        lock (Sync)
        {
            try { return ReadUnsafe(PathForActiveAccount()).OrderByDescending(x => x.StartedAt).Select(Clone).ToList(); }
            catch (Exception ex) { AppLogger.Warning($"Session ledger cannot be read: {ex.Message}"); return Array.Empty<ManagedSessionRecord>(); }
        }
    }

    public static void Start(string id, DateTimeOffset start, string? server, string? selectedClass)
    {
        if (!AccountDataContext.HasActiveAccount || string.IsNullOrWhiteSpace(id)) return;
        lock (Sync)
        {
            var path = PathForActiveAccount();
            var rows = ReadUnsafe(path);
            foreach (var row in rows.Where(x => x.State == "Running" && x.SessionId != id))
            {
                // App previously closed unexpectedly. LastUpdatedAt is the last verified activity.
                row.State = "Interrupted";
                row.EndedAt = row.LastUpdatedAt;
            }
            if (!rows.Any(x => x.SessionId == id)) rows.Add(new ManagedSessionRecord
            {
                SteamId64 = AccountDataContext.ActiveSteamId64!, SessionId = id,
                StartedAt = start, LastUpdatedAt = start,
                Server = server ?? "", SelectedClass = selectedClass ?? "Random"
            });
            SaveUnsafe(path, rows);
        }
    }

    public static void Update(string? id, DateTimeOffset now, TimeSpan eligible, TimeSpan outage, bool finished = false)
    {
        if (!AccountDataContext.HasActiveAccount || string.IsNullOrWhiteSpace(id)) return;
        lock (Sync)
        {
            var path = PathForActiveAccount();
            var rows = ReadUnsafe(path);
            var row = rows.FirstOrDefault(x => x.SessionId == id);
            if (row is null || row.State != "Running") return;
            row.LastUpdatedAt = now;
            row.EligibleSeconds = Math.Max(row.EligibleSeconds, Math.Max(0, eligible.TotalSeconds));
            row.ApiOutageSeconds = Math.Max(row.ApiOutageSeconds, Math.Max(0, outage.TotalSeconds));
            if (finished) { row.State = "Completed"; row.EndedAt = now; }
            SaveUnsafe(path, rows);
        }
    }

    private static List<ManagedSessionRecord> ReadUnsafe(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<ManagedSessionRecord>>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex)
        {
            // Fail closed: a malformed file must never be silently overwritten by [] during the next session.
            throw new InvalidDataException($"Cannot parse session ledger at {path}. Original file was preserved.", ex);
        }
    }

    private static void SaveUnsafe(string path, List<ManagedSessionRecord> rows)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, true);
    }

    private static ManagedSessionRecord Clone(ManagedSessionRecord x) => new()
    {
        SteamId64 = x.SteamId64, SessionId = x.SessionId, StartedAt = x.StartedAt,
        LastUpdatedAt = x.LastUpdatedAt, EndedAt = x.EndedAt,
        Server = x.Server, SelectedClass = x.SelectedClass,
        EligibleSeconds = x.EligibleSeconds, ApiOutageSeconds = x.ApiOutageSeconds, State = x.State
    };
}
