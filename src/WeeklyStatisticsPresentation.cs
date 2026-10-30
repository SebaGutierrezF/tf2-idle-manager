namespace TF2IdleManager;

/// <summary>
/// Communicates locally tracked values without turning a community ~10h
/// reference or the observed interval distribution into a Valve guarantee.
/// </summary>
public static class WeeklyStatisticsPresentation
{
    public static string Remaining(WeeklyUsageSummary usage) =>
        usage.BasePoolLikelyExhausted
            ? "Unknown (reference reached)"
            : usage.EstimatedDropsRemainingHigh == 0
                ? "Unknown (no reliable count)"
                : $"~{usage.EstimatedDropsRemainingLow}-{usage.EstimatedDropsRemainingHigh} (estimate)";

    public static string Next(
        bool managedSessionActive,
        bool monitorDegraded,
        bool pendingItem,
        WeeklyUsageSummary usage,
        WeeklyIntervalStats stats)
    {
        if (!managedSessionActive)
            return "Not idling";
        if (monitorDegraded)
            return "Paused (Steam API unavailable)";
        if (pendingItem)
            return "Paused (pending item)";
        if (usage.BasePoolLikelyExhausted)
            return "No reliable estimate";
        if (stats.EstimatedNextHigh is null || stats.EstimatedNextLow is null)
            return "Insufficient interval data";
        if (stats.EstimatedNextHigh.Value <= TimeSpan.Zero)
            return "Observed window exceeded";
        if (stats.EstimatedNextLow.Value <= TimeSpan.Zero)
            return "Within observed window";
        return $"~{Short(stats.EstimatedNextLow.Value)}–{Short(stats.EstimatedNextHigh.Value)} (estimate)";
    }

    public static string NextCompact(
        bool managedSessionActive,
        bool monitorDegraded,
        bool pendingItem,
        WeeklyUsageSummary usage,
        WeeklyIntervalStats stats)
    {
        var next = Next(managedSessionActive, monitorDegraded, pendingItem, usage, stats);
        return next switch
        {
            "Paused (Steam API unavailable)" => "API paused",
            "Paused (pending item)" => "Pending item",
            "No reliable estimate" => "No ETA",
            "Insufficient interval data" => "No samples",
            "Observed window exceeded" => "Window passed",
            "Within observed window" => "Window open",
            _ => next.Replace(" (estimate)", "", StringComparison.Ordinal)
        };
    }

    private static string Short(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(value.TotalMinutes))}m";
    }
}
