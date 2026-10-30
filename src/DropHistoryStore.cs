using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace TF2IdleManager;

public sealed class DropHistoryEntry
{
    public string SteamId64 { get; set; } = "";
    public ulong ItemId { get; set; }
    public uint DefIndex { get; set; }
    public string DisplayName { get; set; } = "";
    public string? ItemClass { get; set; }
    public string? CraftClass { get; set; }
    // Persisted from v0.4.5 onward so history never has to re-infer category.
    // Values: regular_drop | separate_timer_container
    public string DropType { get; set; } = "";
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? SessionId { get; set; }
    public string? Server { get; set; }
    public double? EligibleSecondsAtDetection { get; set; }

    // Recovered entries are items that the manager first learned about from an
    // already-pending baseline or from a historical acknowledgement log. They count
    // once in history; regular recovered drops count in weekly totals, while containers/cases
    // remain history-only. Their exact original drop time is unknown, so recovered rows are
    // deliberately excluded from personal interval calculations.
    public bool Recovered { get; set; }
    public string? RecoveryReason { get; set; }
    public bool AcknowledgementRecovered { get; set; }

    [JsonIgnore]
    public bool CountsTowardWeeklyMetrics =>
        !string.Equals(EffectiveDropType, Tf2DropClassifier.SeparateTimerContainerType, StringComparison.Ordinal);

    [JsonIgnore]
    public string EffectiveDropType => string.IsNullOrWhiteSpace(DropType)
        ? Tf2DropClassifier.GetDropType(ItemClass, CraftClass, DisplayName)
        : DropType;
}

public static class DropHistoryStore
{
    private static readonly object Sync = new();
    private static List<DropHistoryEntry>? _cache;
    private static string? _loadedPath;
    private static string HistoryPath => AccountDataContext.GetActiveFilePath("drop-history.json");

    private static readonly Regex AcknowledgedLogRegex = new(
        @"^\[(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2})\] \[INFO\] DROP ACKNOWLEDGED \| steamid=(?<steamid>\d{17}) \| itemId=(?<id>\d+) \| defindex=(?<defindex>\d+) \| (?:category=[^|]+ \| )?name=(?<name>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public static void RecordDetected(DropDetectedEvent drop, string? sessionId = null, string? server = null, double? eligibleSecondsAtDetection = null)
    {
        lock (Sync)
        {
            var entries = LoadUnsafe();
            var existing = entries.FirstOrDefault(x => x.ItemId == drop.ItemId);
            if (existing is null)
            {
                entries.Add(new DropHistoryEntry
                {
                    SteamId64 = AccountDataContext.ActiveSteamId64 ?? "",
                    ItemId = drop.ItemId,
                    DefIndex = drop.DefIndex,
                    DisplayName = drop.DisplayName,
                    ItemClass = drop.Item.ItemClass,
                    CraftClass = drop.Item.CraftClass,
                    DropType = Tf2DropClassifier.GetDropType(drop.Item.ItemClass, drop.Item.CraftClass, drop.DisplayName),
                    DetectedAt = drop.DetectedAt,
                    SessionId = sessionId,
                    Server = server,
                    EligibleSecondsAtDetection = eligibleSecondsAtDetection,
                    Recovered = false,
                    RecoveryReason = null
                });
            }
            else
            {
                existing.DefIndex = drop.DefIndex;
                existing.DisplayName = drop.DisplayName;
                existing.ItemClass ??= drop.Item.ItemClass;
                existing.CraftClass ??= drop.Item.CraftClass;
                existing.DropType = Tf2DropClassifier.GetDropType(existing.ItemClass, existing.CraftClass, existing.DisplayName);
                if (existing.DetectedAt == default)
                    existing.DetectedAt = drop.DetectedAt;
                existing.SessionId ??= sessionId;
                existing.Server ??= server;

                // A genuine live detection is more precise than a previously recovered
                // baseline record, so prefer the measured eligible timestamp when it exists.
                if (eligibleSecondsAtDetection is not null)
                    existing.EligibleSecondsAtDetection ??= eligibleSecondsAtDetection;
            }
            SaveUnsafe(entries);
        }
    }

    public static bool RecordRecoveredPending(
        PendingDropItem item,
        DateTimeOffset firstObservedAt,
        string? sessionId = null,
        string? server = null,
        string recoveryReason = "pending-baseline"
    )
    {
        lock (Sync)
        {
            var entries = LoadUnsafe();
            var existing = entries.FirstOrDefault(x => x.ItemId == item.ItemId);
            if (existing is not null)
                return false;

            entries.Add(new DropHistoryEntry
            {
                SteamId64 = AccountDataContext.ActiveSteamId64 ?? "",
                ItemId = item.ItemId,
                DefIndex = item.DefIndex,
                DisplayName = item.DisplayName,
                ItemClass = item.ItemClass,
                CraftClass = item.CraftClass,
                DropType = Tf2DropClassifier.GetDropType(item.ItemClass, item.CraftClass, item.DisplayName),
                DetectedAt = firstObservedAt,
                SessionId = sessionId,
                Server = server,
                EligibleSecondsAtDetection = null,
                Recovered = true,
                RecoveryReason = recoveryReason
            });
            SaveUnsafe(entries);
            return true;
        }
    }

    public static bool RecordAcknowledged(DropAcknowledgedEvent acknowledged)
    {
        lock (Sync)
        {
            var entries = LoadUnsafe();
            var existing = entries.FirstOrDefault(x => x.ItemId == acknowledged.Item.ItemId);
            var recoveredFallback = false;

            if (existing is null)
            {
                // Safety net: an item can be present in the baseline because it dropped while
                // the Steam API/app was unavailable. Do not silently lose it from weekly stats.
                existing = new DropHistoryEntry
                {
                    SteamId64 = AccountDataContext.ActiveSteamId64 ?? "",
                    ItemId = acknowledged.Item.ItemId,
                    DefIndex = acknowledged.Item.DefIndex,
                    DisplayName = acknowledged.Item.DisplayName,
                    ItemClass = acknowledged.Item.ItemClass,
                    CraftClass = acknowledged.Item.CraftClass,
                    DropType = Tf2DropClassifier.GetDropType(acknowledged.Item.ItemClass, acknowledged.Item.CraftClass, acknowledged.Item.DisplayName),
                    DetectedAt = acknowledged.AcknowledgedAt,
                    AcknowledgedAt = acknowledged.AcknowledgedAt,
                    EligibleSecondsAtDetection = null,
                    Recovered = true,
                    RecoveryReason = "acknowledged-without-prior-detection"
                };
                entries.Add(existing);
                recoveredFallback = true;
            }
            else
            {
                existing.AcknowledgedAt = acknowledged.AcknowledgedAt;
                if (string.IsNullOrWhiteSpace(existing.DisplayName) || existing.DisplayName.StartsWith("TF2 item #", StringComparison.Ordinal))
                    existing.DisplayName = acknowledged.Item.DisplayName;
                if (existing.DefIndex == 0)
                    existing.DefIndex = acknowledged.Item.DefIndex;
                existing.ItemClass ??= acknowledged.Item.ItemClass;
                existing.CraftClass ??= acknowledged.Item.CraftClass;
                existing.DropType = Tf2DropClassifier.GetDropType(existing.ItemClass, existing.CraftClass, existing.DisplayName);
            }

            SaveUnsafe(entries);
            return recoveredFallback;
        }
    }

    public static bool ContainsItem(ulong itemId)
    {
        lock (Sync)
            return LoadUnsafe().Any(x => x.ItemId == itemId);
    }

    /// <summary>
    /// Repairs the exact v0.4.1 edge case where a baseline item was acknowledged and logged,
    /// but was never inserted into drop-history.json. This is intentionally idempotent by item ID.
    /// Recovered rows use the acknowledgement timestamp as first-observed time because the real
    /// drop instant is unknowable after the fact, and they are excluded from interval statistics.
    /// </summary>
    public static IReadOnlyList<DropHistoryEntry> ReconcileMissingAcknowledgementsFromLogs(DateTimeOffset now)
    {
        var recovered = new List<DropHistoryEntry>();
        var weekStart = GetTf2WeekStartUtc(now);
        var weekEnd = weekStart.AddDays(7);

        lock (Sync)
        {
            var entries = LoadUnsafe();
            var knownIds = entries.Select(x => x.ItemId).ToHashSet();

            try
            {
                if (!Directory.Exists(AppLogger.LogDirectory))
                    return recovered;

                foreach (var file in Directory.EnumerateFiles(AppLogger.LogDirectory, "tf2-idle-manager-*.log"))
                {
                    IEnumerable<string> lines;
                    try
                    {
                        lines = File.ReadLines(file).ToArray();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var line in lines)
                    {
                        var match = AcknowledgedLogRegex.Match(line);
                        if (!match.Success)
                            continue;

                        var activeSteamId = AccountDataContext.ActiveSteamId64;
                        if (string.IsNullOrWhiteSpace(activeSteamId) ||
                            !string.Equals(match.Groups["steamid"].Value, activeSteamId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!DateTimeOffset.TryParseExact(
                                match.Groups["ts"].Value,
                                "yyyy-MM-dd HH:mm:ss.fff zzz",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out var timestamp))
                        {
                            continue;
                        }

                        if (timestamp < weekStart || timestamp >= weekEnd)
                            continue;
                        if (!ulong.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
                            continue;
                        if (!uint.TryParse(match.Groups["defindex"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var defIndex))
                            continue;
                        if (!knownIds.Add(itemId))
                            continue;

                        var name = match.Groups["name"].Value.Trim();
                        var entry = new DropHistoryEntry
                        {
                            SteamId64 = AccountDataContext.ActiveSteamId64 ?? "",
                            ItemId = itemId,
                            DefIndex = defIndex,
                            DisplayName = string.IsNullOrWhiteSpace(name) ? $"TF2 item #{defIndex}" : name,
                            DropType = Tf2DropClassifier.GetDropType(null, null, string.IsNullOrWhiteSpace(name) ? $"TF2 item #{defIndex}" : name),
                            DetectedAt = timestamp,
                            AcknowledgedAt = timestamp,
                            EligibleSecondsAtDetection = null,
                            Recovered = true,
                            RecoveryReason = "reconciled-from-acknowledgement-log"
                        };
                        entries.Add(entry);
                        recovered.Add(Clone(entry));
                    }
                }

                if (recovered.Count > 0)
                    SaveUnsafe(entries);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Could not reconcile missing acknowledged drops from logs: {ex.Message}");
            }
        }

        return recovered;
    }

    public static WeeklyDropSummary GetCurrentWeek(DateTimeOffset now)
    {
        var start = GetTf2WeekStartUtc(now);
        var end = start.AddDays(7);
        lock (Sync)
        {
            var entries = LoadUnsafe()
                .Where(x => x.DetectedAt >= start && x.DetectedAt < end && x.CountsTowardWeeklyMetrics)
                .ToList();
            return new WeeklyDropSummary(
                start,
                end,
                entries.Count,
                entries.Count(x => x.AcknowledgedAt is not null)
            );
        }
    }

    public static IReadOnlyList<DropHistoryEntry> GetCurrentWeekEntries(DateTimeOffset now)
    {
        var start = GetTf2WeekStartUtc(now);
        var end = start.AddDays(7);
        lock (Sync)
        {
            return LoadUnsafe()
                .Where(x => x.DetectedAt >= start && x.DetectedAt < end)
                .OrderByDescending(x => x.DetectedAt)
                .Select(Clone)
                .ToList();
        }
    }

    public static IReadOnlyList<DropHistoryEntry> GetCurrentWeekRegularEntries(DateTimeOffset now)
    {
        return GetCurrentWeekEntries(now).Where(x => x.CountsTowardWeeklyMetrics).ToList();
    }

    public static IReadOnlyList<DropHistoryEntry> GetCurrentWeekSeparateTimerEntries(DateTimeOffset now)
    {
        return GetCurrentWeekEntries(now).Where(x => !x.CountsTowardWeeklyMetrics).ToList();
    }

    /// <summary>
    /// Reconciles entries that this manager previously observed as pending but that are no
    /// longer present in the current unacknowledged API snapshot. This is especially useful
    /// after switching Steam accounts or reopening the manager: the item was accepted while
    /// this account was not actively monitored, so the exact acknowledgement instant is unknown.
    /// </summary>
    public static IReadOnlyList<DropHistoryEntry> ReconcileAcknowledgementState(DropSnapshot snapshot, DateTimeOffset reconciledAt)
    {
        var changed = new List<DropHistoryEntry>();
        var pendingIds = snapshot.PendingItems.Select(x => x.ItemId).ToHashSet();

        lock (Sync)
        {
            var entries = LoadUnsafe();
            foreach (var entry in entries.Where(x => x.AcknowledgedAt is null && !pendingIds.Contains(x.ItemId)))
            {
                entry.AcknowledgedAt = reconciledAt;
                entry.AcknowledgementRecovered = true;
                changed.Add(Clone(entry));
            }

            if (changed.Count > 0)
                SaveUnsafe(entries);
        }

        return changed;
    }

    public static WeeklyIntervalStats GetCurrentWeekIntervalStats(DateTimeOffset now, TimeSpan currentEligibleTime)
    {
        var start = GetTf2WeekStartUtc(now);
        var end = start.AddDays(7);
        lock (Sync)
        {
            var entries = LoadUnsafe()
                .Where(x => x.DetectedAt >= start && x.DetectedAt < end && x.CountsTowardWeeklyMetrics && !x.Recovered && x.EligibleSecondsAtDetection is not null)
                .OrderBy(x => x.EligibleSecondsAtDetection)
                .ToList();

            var intervals = new List<TimeSpan>();
            for (var i = 1; i < entries.Count; i++)
            {
                var previous = entries[i - 1].EligibleSecondsAtDetection!.Value;
                var current = entries[i].EligibleSecondsAtDetection!.Value;
                var seconds = current - previous;
                if (seconds > 0 && seconds <= TimeSpan.FromHours(3).TotalSeconds)
                    intervals.Add(TimeSpan.FromSeconds(seconds));
            }

            TimeSpan? average = intervals.Count == 0
                ? null
                : TimeSpan.FromSeconds(intervals.Average(x => x.TotalSeconds));
            TimeSpan? shortest = intervals.Count == 0 ? null : intervals.Min();
            TimeSpan? longest = intervals.Count == 0 ? null : intervals.Max();

            TimeSpan? sinceLast = null;
            if (entries.Count > 0)
            {
                var lastEligible = entries[^1].EligibleSecondsAtDetection!.Value;
                sinceLast = TimeSpan.FromSeconds(Math.Max(0, currentEligibleTime.TotalSeconds - lastEligible));
            }

            var lowMinutes = 30.0;
            var highMinutes = 70.0;
            if (intervals.Count >= 2)
            {
                lowMinutes = Math.Clamp(shortest!.Value.TotalMinutes, 20, 90);
                highMinutes = Math.Clamp(longest!.Value.TotalMinutes, lowMinutes, 120);
            }

            TimeSpan? nextLow = null;
            TimeSpan? nextHigh = null;
            if (sinceLast is not null)
            {
                nextLow = TimeSpan.FromMinutes(Math.Max(0, lowMinutes - sinceLast.Value.TotalMinutes));
                nextHigh = TimeSpan.FromMinutes(Math.Max(0, highMinutes - sinceLast.Value.TotalMinutes));
            }

            return new WeeklyIntervalStats(
                intervals.Count,
                average,
                shortest,
                longest,
                sinceLast,
                nextLow,
                nextHigh
            );
        }
    }

    public static IReadOnlyList<DropHistoryEntry> GetAllEntries()
    {
        lock (Sync)
        {
            return LoadUnsafe().OrderByDescending(x => x.DetectedAt)
                .Select(x => new DropHistoryEntry
                {
                    SteamId64 = x.SteamId64, ItemId = x.ItemId, DefIndex = x.DefIndex,
                    DisplayName = x.DisplayName, ItemClass = x.ItemClass, CraftClass = x.CraftClass,
                    DropType = x.DropType, DetectedAt = x.DetectedAt, AcknowledgedAt = x.AcknowledgedAt,
                    SessionId = x.SessionId, Server = x.Server, Recovered = x.Recovered,
                    RecoveryReason = x.RecoveryReason, AcknowledgementRecovered = x.AcknowledgementRecovered,
                    EligibleSecondsAtDetection = x.EligibleSecondsAtDetection
                }).ToList();
        }
    }

    public static IReadOnlyList<DropHistoryEntry> GetSessionEntries(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Array.Empty<DropHistoryEntry>();

        lock (Sync)
        {
            return LoadUnsafe()
                .Where(x => string.Equals(x.SessionId, sessionId, StringComparison.Ordinal))
                .OrderBy(x => x.DetectedAt)
                .Select(Clone)
                .ToList();
        }
    }

    public static DateTimeOffset GetTf2WeekStartUtc(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        var daysSinceThursday = ((int)utc.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
        var start = new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-daysSinceThursday);
        return new DateTimeOffset(start);
    }

    private static DropHistoryEntry Clone(DropHistoryEntry source) => new()
    {
        SteamId64 = source.SteamId64,
        ItemId = source.ItemId,
        DefIndex = source.DefIndex,
        DisplayName = source.DisplayName,
        ItemClass = source.ItemClass,
        CraftClass = source.CraftClass,
        DropType = source.EffectiveDropType,
        DetectedAt = source.DetectedAt,
        AcknowledgedAt = source.AcknowledgedAt,
        SessionId = source.SessionId,
        Server = source.Server,
        EligibleSecondsAtDetection = source.EligibleSecondsAtDetection,
        Recovered = source.Recovered,
        RecoveryReason = source.RecoveryReason,
        AcknowledgementRecovered = source.AcknowledgementRecovered
    };

    private static List<DropHistoryEntry> LoadUnsafe()
    {
        var path = HistoryPath;
        if (_cache is not null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            return _cache;

        _cache = null;
        _loadedPath = path;

        try
        {
            if (!File.Exists(path))
                return _cache = new List<DropHistoryEntry>();
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<DropHistoryEntry>>(json) ?? new List<DropHistoryEntry>();
            var activeSteamId = AccountDataContext.ActiveSteamId64 ?? "";
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.SteamId64))
                    entry.SteamId64 = activeSteamId;
                if (string.IsNullOrWhiteSpace(entry.DropType))
                    entry.DropType = Tf2DropClassifier.GetDropType(entry.ItemClass, entry.CraftClass, entry.DisplayName);
            }
            return _cache = entries;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not load drop history for {AccountDataContext.DescribeActiveAccount()}: {ex.Message}");
            return _cache = new List<DropHistoryEntry>();
        }
    }

    private static void SaveUnsafe(List<DropHistoryEntry> entries)
    {
        try
        {
            var path = HistoryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var activeSteamId = AccountDataContext.ActiveSteamId64 ?? "";
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.SteamId64))
                    entry.SteamId64 = activeSteamId;
                if (string.IsNullOrWhiteSpace(entry.DropType))
                    entry.DropType = Tf2DropClassifier.GetDropType(entry.ItemClass, entry.CraftClass, entry.DisplayName);
            }
            var json = JsonSerializer.Serialize(entries.OrderBy(x => x.DetectedAt).ToList(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
            _loadedPath = path;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not save drop history: {ex.Message}");
        }
    }
}
