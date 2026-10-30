namespace TF2IdleManager;

public sealed class AppSettings
{
    public string ServerAddress { get; set; } = "";
    public bool AttackEnabled { get; set; } = true;
    public bool LeftEnabled { get; set; } = true;
    // Legacy field retained for settings migration only. The actual selection is AutoClass.
    public bool AutoSelectHeavy { get; set; } = true;
    // Empty = v0.4.x settings; migrate to Heavy when legacy auto-selection was enabled.
    public string AutoClass { get; set; } = "";
    public bool DarkMode { get; set; } = false;
    public bool RepeatDropAlerts { get; set; } = false;
    public bool RepeatUntilAcknowledged { get; set; } = false;
    public int RepeatCount { get; set; } = 3; // Additional alerts after the first notification.
    public int RepeatIntervalSeconds { get; set; } = 20;
    public bool ShowApiStatusNotifications { get; set; } = true;
    // App-owned popups (drops, repeats, API and watchdog): user-selected display corner.
    public string NotificationCorner { get; set; } = "BottomLeft";
    public List<string> ServerProfiles { get; set; } = new();
    public bool MinimizeTf2AfterLaunch { get; set; } = true;
    public bool CloseTf2OnStop { get; set; } = true;
    public int WindowWidth { get; set; } = 640;
    public int WindowHeight { get; set; } = 480;
    public string ExtraLaunchOptions { get; set; } = "";
    public bool DropNotificationsEnabled { get; set; } = true;
    public bool AutoReconnectEnabled { get; set; } = true;
    // Explicit opt-in. Only servers saved in Preferences > Servers may be used as backups.
    public bool TryBackupServersWhenFull { get; set; } = false;
    // A full/loading server is not a verified idle session. Set generously for slower machines.
    public int ServerJoinTimeoutSeconds { get; set; } = 240;

    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveClass => Tf2Classes.Normalize(AutoClass);
}

public static class Tf2Classes
{
    // TF2 class-menu digits 1-9. Random is resolved to one of them at connection.
    private static readonly (string Name, ushort Key, string Cfg)[] Choices =
    {
        ("Random", 0x30, ""),
        ("Scout", 0x31, "scout.cfg"),
        ("Soldier", 0x32, "soldier.cfg"),
        ("Pyro", 0x33, "pyro.cfg"),
        ("Demoman", 0x34, "demoman.cfg"),
        ("Heavy", 0x35, "heavyweapons.cfg"),
        ("Engineer", 0x36, "engineer.cfg"),
        ("Medic", 0x37, "medic.cfg"),
        ("Sniper", 0x38, "sniper.cfg"),
        ("Spy", 0x39, "spy.cfg")
    };
    public static IReadOnlyList<string> Names { get; } = Choices.Select(x => x.Name).ToArray();
    public static string Normalize(string? choice) => Choices.Any(x => string.Equals(x.Name, choice, StringComparison.OrdinalIgnoreCase))
        ? Choices.First(x => string.Equals(x.Name, choice, StringComparison.OrdinalIgnoreCase)).Name
        : "Random";
    public static ushort Key(string? choice) => Choices.First(x => x.Name == Normalize(choice)).Key;
    public static string Cfg(string? choice) => Choices.First(x => x.Name == Normalize(choice)).Cfg;
    public static string Display(string? choice) => Normalize(choice);
}

public sealed record SteamDetection(
    string? SteamDirectory,
    string? SteamExe,
    string? Tf2Directory,
    string? Tf2CfgDirectory,
    string? Tf2ConsoleLog,
    string? PersonaName,
    string? SteamId64
)
{
    public bool SteamFound => !string.IsNullOrWhiteSpace(SteamExe) && File.Exists(SteamExe);
    public bool Tf2Found => !string.IsNullOrWhiteSpace(Tf2Directory) && Directory.Exists(Tf2Directory);
    public bool Ready => SteamFound && Tf2Found && !string.IsNullOrWhiteSpace(Tf2CfgDirectory);
}

public static class Tf2DropClassifier
{
    public const string RegularDropType = "regular_drop";
    public const string SeparateTimerContainerType = "separate_timer_container";

    private static readonly HashSet<string> ContainerNameTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "crate", "case", "caja", "cajón", "cajon", "estuche"
    };

    public static string GetDropType(string? itemClass, string? craftClass, string? displayName) =>
        IsSeparateTimerContainer(itemClass, craftClass, displayName)
            ? SeparateTimerContainerType
            : RegularDropType;

    public static bool IsSeparateTimerContainer(string? itemClass, string? craftClass, string? displayName)
    {
        if (string.Equals(itemClass, "supply_crate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(craftClass, "supply_crate", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        var tokens = displayName.Split(
            new[] { ' ', '-', ':', ';', ',', '.', '(', ')', '[', ']', '{', '}' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(ContainerNameTokens.Contains);
    }
}

public sealed record PendingDropItem(
    ulong ItemId,
    uint DefIndex,
    uint InventoryPosition,
    int Origin,
    string? ResolvedName,
    string? ItemClass = null,
    string? CraftClass = null,
    string? ImageUrl = null
)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ResolvedName)
        ? $"TF2 item #{DefIndex}"
        : ResolvedName!;

    public bool IsSeparateTimerContainer =>
        Tf2DropClassifier.IsSeparateTimerContainer(ItemClass, CraftClass, DisplayName);

    public bool CountsTowardWeeklyMetrics => !IsSeparateTimerContainer;
}

public sealed record DropDetectedEvent(
    PendingDropItem Item,
    DateTimeOffset DetectedAt
)
{
    public ulong ItemId => Item.ItemId;
    public uint DefIndex => Item.DefIndex;
    public uint InventoryPosition => Item.InventoryPosition;
    public int Origin => Item.Origin;
    public string DisplayName => Item.DisplayName;
}

public sealed record DropAcknowledgedEvent(
    PendingDropItem Item,
    DateTimeOffset AcknowledgedAt
);

public sealed record DropSnapshot(
    IReadOnlyList<PendingDropItem> PendingItems,
    DateTimeOffset CheckedAt
)
{
    public int PendingCount => PendingItems.Count;
}

public sealed record DropBaselineEvent(
    DropSnapshot Snapshot,
    bool RecoveredAfterApiOutage
);

public sealed record DropMonitorAvailabilityEvent(
    bool Available,
    int ConsecutiveFailures,
    DateTimeOffset ChangedAt,
    DateTimeOffset? OutageStartedAt,
    string Message
);

public sealed record WeeklyDropSummary(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int DetectedCount,
    int AcknowledgedCount
);

public sealed record WeeklyIntervalStats(
    int SampleCount,
    TimeSpan? AverageInterval,
    TimeSpan? ShortestInterval,
    TimeSpan? LongestInterval,
    TimeSpan? EligibleSinceLastDrop,
    TimeSpan? EstimatedNextLow,
    TimeSpan? EstimatedNextHigh
);

public enum ManagedSessionHealth
{
    Healthy,
    ProcessExited,
    Disconnected,
    ServerTransition,
    ServerFull,
    ServerJoinStalled
}

public sealed record WeeklyUsageSummary(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    TimeSpan ManagedTime,
    TimeSpan EligibleTime,
    TimeSpan EstimatedBaseTimeRemaining,
    int EstimatedDropsRemainingLow,
    int EstimatedDropsRemainingHigh,
    bool BasePoolLikelyExhausted
);
