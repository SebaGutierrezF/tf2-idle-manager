using System.Text.Json;
using System.Text.RegularExpressions;

namespace TF2IdleManager;

public sealed record LegacyAccountMigrationResult(
    bool LegacyDataFound,
    bool Migrated,
    bool Ambiguous,
    string? TargetSteamId64,
    string Message
);

public static class LegacyAccountMigration
{
    private sealed class MigrationMarker
    {
        public DateTimeOffset MigratedAt { get; set; }
        public List<string> SteamIds { get; set; } = new();
        public int HistoryRowsAssigned { get; set; }
        public int HistoryRowsUnassigned { get; set; }
        public string? WeeklyUsageAssignedTo { get; set; }
    }

    private static readonly Regex MonitorStartRegex = new(
        @"Drop monitor started \| source=Steam Web API \| steamid=(?<steamid>\d{17}) \|",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex ItemEventRegex = new(
        @"DROP (?:DETECTED|ACKNOWLEDGED) \|(?: source=Steam Web API \|)?(?: steamid=(?<steamid>\d{17}) \|)? itemId=(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static string MarkerPath => Path.Combine(AppLogger.AppDataDirectory, "account-data-migration-v0.4.3.json");
    private static string LegacyHistoryPath => Path.Combine(AppLogger.AppDataDirectory, "drop-history.json");
    private static string LegacyUsagePath => Path.Combine(AppLogger.AppDataDirectory, "weekly-usage.json");

    public static LegacyAccountMigrationResult TryMigrateGlobalData()
    {
        var legacyHistoryExists = File.Exists(LegacyHistoryPath);
        var legacyUsageExists = File.Exists(LegacyUsagePath);
        if (!legacyHistoryExists && !legacyUsageExists)
            return new LegacyAccountMigrationResult(false, false, false, null, "No legacy global account data was found.");

        try
        {
            if (File.Exists(MarkerPath))
            {
                var marker = JsonSerializer.Deserialize<MigrationMarker>(File.ReadAllText(MarkerPath));
                if (marker is not null)
                {
                    var mapped = marker.SteamIds.Count == 0 ? "none" : string.Join(", ", marker.SteamIds);
                    return new LegacyAccountMigrationResult(
                        true,
                        false,
                        marker.HistoryRowsUnassigned > 0,
                        marker.SteamIds.Count == 1 ? marker.SteamIds[0] : null,
                        $"Legacy global data migration already ran. Account(s): {mapped}; assigned rows={marker.HistoryRowsAssigned}; unassigned rows={marker.HistoryRowsUnassigned}."
                    );
                }
            }
        }
        catch
        {
            // A damaged marker should not prevent a safe retry.
        }

        var itemOwners = new Dictionary<ulong, string>();
        var observedSteamIds = new HashSet<string>(StringComparer.Ordinal);
        BuildLegacyOwnershipMap(itemOwners, observedSteamIds);

        var assignedRows = 0;
        var unassignedRows = 0;
        var rowsByAccount = new Dictionary<string, List<DropHistoryEntry>>(StringComparer.Ordinal);

        if (legacyHistoryExists)
        {
            try
            {
                var legacyEntries = JsonSerializer.Deserialize<List<DropHistoryEntry>>(File.ReadAllText(LegacyHistoryPath))
                    ?? new List<DropHistoryEntry>();

                foreach (var entry in legacyEntries)
                {
                    string? owner = null;
                    if (AccountDataContext.IsValidSteamId64(entry.SteamId64))
                        owner = entry.SteamId64;
                    else if (itemOwners.TryGetValue(entry.ItemId, out var mappedOwner))
                        owner = mappedOwner;

                    if (owner is null)
                    {
                        unassignedRows++;
                        continue;
                    }

                    observedSteamIds.Add(owner);
                    entry.SteamId64 = owner;
                    if (!rowsByAccount.TryGetValue(owner, out var rows))
                    {
                        rows = new List<DropHistoryEntry>();
                        rowsByAccount[owner] = rows;
                    }
                    rows.Add(entry);
                    assignedRows++;
                }

                foreach (var pair in rowsByAccount)
                    MergeHistoryIntoAccount(pair.Key, pair.Value);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Could not split legacy drop-history.json by Steam account: {ex.Message}");
            }
        }

        // weekly-usage.json did not contain an account ID before v0.4.3. Assign it only
        // when there is one unambiguous owner of the historical drop rows / monitor logs.
        string? usageOwner = null;
        if (legacyUsageExists)
        {
            var accountsWithRows = rowsByAccount.Where(x => x.Value.Count > 0).Select(x => x.Key).Distinct().ToList();
            if (accountsWithRows.Count == 1)
                usageOwner = accountsWithRows[0];
            else if (accountsWithRows.Count == 0 && observedSteamIds.Count == 1)
                usageOwner = observedSteamIds.Single();

            if (usageOwner is not null)
            {
                var destination = Path.Combine(AccountDataContext.GetAccountDirectory(usageOwner), "weekly-usage.json");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination))
                    File.Copy(LegacyUsagePath, destination, overwrite: false);
            }
        }

        var markerData = new MigrationMarker
        {
            MigratedAt = DateTimeOffset.Now,
            SteamIds = observedSteamIds.OrderBy(x => x).ToList(),
            HistoryRowsAssigned = assignedRows,
            HistoryRowsUnassigned = unassignedRows,
            WeeklyUsageAssignedTo = usageOwner
        };
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(markerData, new JsonSerializerOptions { WriteIndented = true }));

        var accountsText = observedSteamIds.Count == 0 ? "none identified" : string.Join(", ", observedSteamIds.OrderBy(x => x));
        var usageText = usageOwner is null
            ? "legacy weekly-usage was left unassigned because its owner was ambiguous"
            : $"legacy weekly-usage was assigned to {usageOwner}";
        var ambiguous = unassignedRows > 0 || (legacyUsageExists && usageOwner is null);

        return new LegacyAccountMigrationResult(
            true,
            assignedRows > 0 || usageOwner is not null,
            ambiguous,
            observedSteamIds.Count == 1 ? observedSteamIds.Single() : null,
            $"Legacy v0.4.2 data split by retained monitor logs. Accounts: {accountsText}; history rows assigned={assignedRows}, unassigned={unassignedRows}; {usageText}. Root legacy files remain untouched as backup."
        );
    }

    private static void BuildLegacyOwnershipMap(Dictionary<ulong, string> itemOwners, HashSet<string> observedSteamIds)
    {
        if (!Directory.Exists(AppLogger.LogDirectory))
            return;

        string? activeMonitorSteamId = null;
        foreach (var file in Directory.EnumerateFiles(AppLogger.LogDirectory, "tf2-idle-manager-*.log").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    var monitor = MonitorStartRegex.Match(line);
                    if (monitor.Success)
                    {
                        activeMonitorSteamId = monitor.Groups["steamid"].Value;
                        if (AccountDataContext.IsValidSteamId64(activeMonitorSteamId))
                            observedSteamIds.Add(activeMonitorSteamId);
                        continue;
                    }

                    var itemEvent = ItemEventRegex.Match(line);
                    if (!itemEvent.Success || !ulong.TryParse(itemEvent.Groups["id"].Value, out var itemId))
                        continue;

                    var explicitSteamId = itemEvent.Groups["steamid"].Success
                        ? itemEvent.Groups["steamid"].Value
                        : null;
                    var owner = AccountDataContext.IsValidSteamId64(explicitSteamId)
                        ? explicitSteamId
                        : activeMonitorSteamId;

                    if (!AccountDataContext.IsValidSteamId64(owner))
                        continue;

                    observedSteamIds.Add(owner!);
                    itemOwners[itemId] = owner!;
                }
            }
            catch
            {
                // Best effort per retained log file.
            }
        }
    }

    private static void MergeHistoryIntoAccount(string steamId64, List<DropHistoryEntry> incoming)
    {
        var directory = AccountDataContext.GetAccountDirectory(steamId64);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "drop-history.json");

        var merged = new Dictionary<ulong, DropHistoryEntry>();
        if (File.Exists(path))
        {
            try
            {
                foreach (var entry in JsonSerializer.Deserialize<List<DropHistoryEntry>>(File.ReadAllText(path)) ?? new List<DropHistoryEntry>())
                    merged[entry.ItemId] = entry;
            }
            catch
            {
                // If an account file is damaged, preserve it and avoid overwriting it.
                var backup = path + $".invalid-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Copy(path, backup, overwrite: false);
            }
        }

        foreach (var entry in incoming)
        {
            entry.SteamId64 = steamId64;
            if (!merged.ContainsKey(entry.ItemId))
                merged[entry.ItemId] = entry;
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(merged.Values.OrderBy(x => x.DetectedAt).ToList(), new JsonSerializerOptions { WriteIndented = true })
        );
    }
}
