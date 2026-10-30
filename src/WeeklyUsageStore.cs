using System.Text.Json;

namespace TF2IdleManager;

public sealed class WeeklyUsageBucket
{
    public string SteamId64 { get; set; } = "";
    public DateTimeOffset WindowStartUtc { get; set; }
    public double ManagedSeconds { get; set; }
    public double EligibleSeconds { get; set; }
}

public static class WeeklyUsageStore
{
    private static readonly object Sync = new();
    private static List<WeeklyUsageBucket>? _cache;
    private static string? _loadedPath;
    private static DateTimeOffset? _lastTick;
    private static DateTimeOffset? _lastSavedAt;
    private static bool _sessionActive;
    // Session counters have their own lifecycle. They never subtract a weekly bucket,
    // so Thursday UTC rollover cannot make an active session jump back to zero.
    private static double _sessionManagedSeconds;
    private static double _sessionEligibleSeconds;

    // The real TF2 cap is not exposed through an API. This is deliberately an estimate
    // for the commonly documented base weekly playtime pool, not a promise of drops.
    private static readonly TimeSpan EstimatedBaseWeeklyPool = TimeSpan.FromHours(10);

    private static string UsagePath => AccountDataContext.GetActiveFilePath("weekly-usage.json");

    public static void StartSession(DateTimeOffset now)
    {
        lock (Sync)
        {
            EnsureBucketUnsafe(now);
            _sessionManagedSeconds = 0;
            _sessionEligibleSeconds = 0;
            ResumeSessionUnsafe(now);
        }
    }

    // A watchdog recovery is part of the same managed session: resume after a gap
    // without counting the downtime or clearing the prior session's totals.
    public static void ResumeSession(DateTimeOffset now)
    {
        lock (Sync)
        {
            EnsureBucketUnsafe(now);
            ResumeSessionUnsafe(now);
        }
    }

    private static void ResumeSessionUnsafe(DateTimeOffset now)
    {
        _lastTick = now;
        _lastSavedAt = now;
        _sessionActive = true;
    }

    public static TimeSpan GetSessionEligibleTime()
    {
        lock (Sync)
            return TimeSpan.FromSeconds(Math.Max(0, _sessionEligibleSeconds));
    }

    public static void Tick(DateTimeOffset now, bool eligibleForDrops)
    {
        lock (Sync)
        {
            TickUnsafe(now, eligibleForDrops);
        }
    }

    public static void StopSession(DateTimeOffset now, bool eligibleForDrops)
    {
        lock (Sync)
        {
            if (_sessionActive)
            {
                TickUnsafe(now, eligibleForDrops);
                _sessionActive = false;
                _lastTick = null;
                SaveUnsafe();
                _lastSavedAt = now;
            }
        }
    }

    public static TimeSpan GetEligibleTime(DateTimeOffset now)
    {
        lock (Sync)
        {
            var bucket = EnsureBucketUnsafe(now);
            return TimeSpan.FromSeconds(Math.Max(0, bucket.EligibleSeconds));
        }
    }

    // Read-only snapshots for the presentation layer. These use the active
    // SteamID64's own file and do not create, reset or alter week buckets.
    public static IReadOnlyList<WeeklyUsageBucket> GetRecordedWeeks()
    {
        lock (Sync)
        {
            return LoadUnsafe()
                .Where(b => string.Equals(b.SteamId64, AccountDataContext.ActiveSteamId64,
                    StringComparison.Ordinal))
                .OrderByDescending(b => b.WindowStartUtc)
                .Select(b => new WeeklyUsageBucket
                {
                    SteamId64 = b.SteamId64,
                    WindowStartUtc = b.WindowStartUtc,
                    ManagedSeconds = b.ManagedSeconds,
                    EligibleSeconds = b.EligibleSeconds
                })
                .ToList();
        }
    }

    public static WeeklyUsageSummary GetCurrentWeek(DateTimeOffset now, int detectedDrops)
    {
        lock (Sync)
        {
            var bucket = EnsureBucketUnsafe(now);
            var managed = TimeSpan.FromSeconds(Math.Max(0, bucket.ManagedSeconds));
            var eligible = TimeSpan.FromSeconds(Math.Max(0, bucket.EligibleSeconds));
            var remaining = EstimatedBaseWeeklyPool - eligible;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            var fractionRemaining = EstimatedBaseWeeklyPool.TotalSeconds <= 0
                ? 0
                : Math.Clamp(remaining.TotalSeconds / EstimatedBaseWeeklyPool.TotalSeconds, 0, 1);

            // Community documentation generally estimates roughly 8-12 normal drops in a week.
            // Scale that range by tracked base-pool time and also by already observed drops.
            var byTimeLow = (int)Math.Ceiling(8 * fractionRemaining);
            var byTimeHigh = (int)Math.Ceiling(12 * fractionRemaining);
            var byCountLow = Math.Max(0, 8 - detectedDrops);
            var byCountHigh = Math.Max(0, 12 - detectedDrops);
            var low = Math.Min(byTimeLow, byCountLow);
            var high = Math.Min(byTimeHigh, byCountHigh);
            if (high < low)
                high = low;

            var start = bucket.WindowStartUtc;
            return new WeeklyUsageSummary(
                start,
                start.AddDays(7),
                managed,
                eligible,
                remaining,
                low,
                high,
                eligible >= EstimatedBaseWeeklyPool
            );
        }
    }

    private static void TickUnsafe(DateTimeOffset now, bool eligibleForDrops)
    {
        if (!_sessionActive || _lastTick is null)
            return;

        var previous = _lastTick.Value;
        _lastTick = now;
        var delta = now - previous;
        if (delta <= TimeSpan.Zero)
            return;

        // Ignore large sleep/debugger gaps. Attribute at most the latest 90 seconds,
        // while preserving an exact split if that small interval straddles Thursday UTC.
        if (delta > TimeSpan.FromSeconds(90))
            previous = now - TimeSpan.FromSeconds(90);
        delta = now - previous;

        _sessionManagedSeconds += delta.TotalSeconds;
        if (eligibleForDrops)
            _sessionEligibleSeconds += delta.TotalSeconds;

        var cursor = previous.ToUniversalTime();
        var end = now.ToUniversalTime();
        bool crossedWeek = DropHistoryStore.GetTf2WeekStartUtc(cursor)
            != DropHistoryStore.GetTf2WeekStartUtc(end);
        while (cursor < end)
        {
            var bucket = EnsureBucketUnsafe(cursor);
            var boundary = bucket.WindowStartUtc.AddDays(7);
            var until = end < boundary ? end : boundary;
            var seconds = (until - cursor).TotalSeconds;
            bucket.ManagedSeconds += seconds;
            if (eligibleForDrops)
                bucket.EligibleSeconds += seconds;
            cursor = until;
        }

        // Save the closed week's final seconds promptly, rather than relying on
        // the next 30-second periodic flush after crossing a weekly boundary.
        if (crossedWeek || _lastSavedAt is null || now - _lastSavedAt.Value >= TimeSpan.FromSeconds(30))
        {
            SaveUnsafe();
            _lastSavedAt = now;
        }
    }

    private static WeeklyUsageBucket EnsureBucketUnsafe(DateTimeOffset now)
    {
        var start = DropHistoryStore.GetTf2WeekStartUtc(now);
        var buckets = LoadUnsafe();
        var bucket = buckets.FirstOrDefault(x => x.WindowStartUtc == start);
        if (bucket is null)
        {
            bucket = new WeeklyUsageBucket { SteamId64 = AccountDataContext.ActiveSteamId64 ?? "", WindowStartUtc = start };
            buckets.Add(bucket);
            buckets.RemoveAll(x => x.WindowStartUtc < start.AddDays(-84));
            SaveUnsafe();
        }
        return bucket;
    }

    private static List<WeeklyUsageBucket> LoadUnsafe()
    {
        var path = UsagePath;
        if (_cache is not null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            return _cache;

        // Account changes are only allowed while no managed session is active. Reset
        // in-memory timing state when the backing account path changes so time can
        // never leak from one SteamID64 into another.
        _cache = null;
        _loadedPath = path;
        _lastTick = null;
        _lastSavedAt = null;
        _sessionActive = false;
        _sessionEligibleSeconds = 0;
        _sessionManagedSeconds = 0;

        try
        {
            if (!File.Exists(path))
                return _cache = new List<WeeklyUsageBucket>();
            var json = File.ReadAllText(path);
            var buckets = JsonSerializer.Deserialize<List<WeeklyUsageBucket>>(json) ?? new List<WeeklyUsageBucket>();
            var activeSteamId = AccountDataContext.ActiveSteamId64 ?? "";
            foreach (var bucket in buckets)
            {
                if (string.IsNullOrWhiteSpace(bucket.SteamId64))
                    bucket.SteamId64 = activeSteamId;
            }
            return _cache = buckets;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not load weekly usage for {AccountDataContext.DescribeActiveAccount()}: {ex.Message}");
            return _cache = new List<WeeklyUsageBucket>();
        }
    }

    private static void SaveUnsafe()
    {
        try
        {
            var path = UsagePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var activeSteamId = AccountDataContext.ActiveSteamId64 ?? "";
            var data = (_cache ?? new List<WeeklyUsageBucket>())
                .OrderBy(x => x.WindowStartUtc)
                .ToList();
            foreach (var bucket in data)
            {
                if (string.IsNullOrWhiteSpace(bucket.SteamId64))
                    bucket.SteamId64 = activeSteamId;
            }
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            _loadedPath = path;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not save weekly usage: {ex.Message}");
        }
    }
}
