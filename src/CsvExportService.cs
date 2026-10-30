using System.Globalization;
using System.Text;

namespace TF2IdleManager;

// Excel-friendly UTF-8 BOM CSV. Export is read-only and SteamID64-scoped.
public static class CsvExportService
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static string Cell(object? value)
    {
        var s = Convert.ToString(value, Invariant) ?? "";
        // Prevent spreadsheet formula execution when names/servers originate externally.
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    private static string Line(params object?[] values) => string.Join(",", values.Select(Cell));

    public static void ExportDrops(string path, IEnumerable<DropHistoryEntry> rows)
    {
        var lines = new List<string> { Line("steamid64", "item_id", "defindex", "name", "category", "detected_local", "acknowledged_local", "state", "session_id", "server", "recovered", "recovery_reason") };
        lines.AddRange(rows.Select(x => Line(x.SteamId64, x.ItemId, x.DefIndex, x.DisplayName,
            x.CountsTowardWeeklyMetrics ? "regular" : "container", x.DetectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", Invariant),
            x.AcknowledgedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", Invariant),
            x.AcknowledgedAt.HasValue ? "accepted" : "pending", x.SessionId, x.Server, x.Recovered, x.RecoveryReason)));
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void ExportSessions(string path, IEnumerable<ManagedSessionRecord> sessions,
        IReadOnlyList<DropHistoryEntry> drops)
    {
        var grouped = drops.Where(x => !string.IsNullOrWhiteSpace(x.SessionId))
            .GroupBy(x => x.SessionId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList());
        var lines = new List<string> { Line("steamid64", "session_id", "start_local", "end_local", "state", "wall_minutes", "eligible_minutes", "api_outage_minutes", "server", "selected_class", "regular_drops", "containers", "accepted_items") };
        foreach (var s in sessions)
        {
            var items = grouped.GetValueOrDefault(s.SessionId) ?? new List<DropHistoryEntry>();
            lines.Add(Line(s.SteamId64, s.SessionId, s.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", Invariant),
                s.EndedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", Invariant), s.State,
                Math.Max(0, ((s.EndedAt ?? s.LastUpdatedAt) - s.StartedAt).TotalMinutes).ToString("0.0", Invariant),
                (s.EligibleSeconds / 60).ToString("0.0", Invariant),
                (s.ApiOutageSeconds / 60).ToString("0.0", Invariant),
                s.Server, s.SelectedClass, items.Count(x => x.CountsTowardWeeklyMetrics),
                items.Count(x => !x.CountsTowardWeeklyMetrics), items.Count(x => x.AcknowledgedAt.HasValue)));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
