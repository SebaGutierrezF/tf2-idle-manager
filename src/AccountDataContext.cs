using System.Text.Json;

namespace TF2IdleManager;

public sealed class AccountMetadata
{
    public string SteamId64 { get; set; } = "";
    public string PersonaName { get; set; } = "";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public static class AccountDataContext
{
    private static readonly object Sync = new();

    public static string AccountsRoot => Path.Combine(AppLogger.AppDataDirectory, "accounts");
    public static string? ActiveSteamId64 { get; private set; }
    public static string? ActivePersonaName { get; private set; }
    public static bool HasActiveAccount => IsValidSteamId64(ActiveSteamId64);

    public static bool Activate(string? steamId64, string? personaName)
    {
        var normalized = NormalizeSteamId64(steamId64);
        lock (Sync)
        {
            var changed = !string.Equals(ActiveSteamId64, normalized, StringComparison.Ordinal);
            ActiveSteamId64 = normalized;
            ActivePersonaName = personaName?.Trim() ?? string.Empty;

            Directory.CreateDirectory(AccountsRoot);
            Directory.CreateDirectory(GetAccountDirectory(normalized));
            UpdateMetadataUnsafe(normalized, ActivePersonaName);
            return changed;
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            ActiveSteamId64 = null;
            ActivePersonaName = null;
        }
    }

    public static string GetActiveFilePath(string fileName)
    {
        lock (Sync)
        {
            if (!HasActiveAccount)
                throw new InvalidOperationException("No active Steam account data context is available.");
            return Path.Combine(GetAccountDirectory(ActiveSteamId64!), fileName);
        }
    }

    public static string GetAccountDirectory(string steamId64)
    {
        var normalized = NormalizeSteamId64(steamId64);
        return Path.Combine(AccountsRoot, normalized);
    }

    public static string DescribeActiveAccount()
    {
        lock (Sync)
        {
            if (!HasActiveAccount)
                return "Steam account unavailable";
            return string.IsNullOrWhiteSpace(ActivePersonaName)
                ? ActiveSteamId64!
                : $"{ActivePersonaName} ({ActiveSteamId64})";
        }
    }

    public static bool IsValidSteamId64(string? steamId64)
        => !string.IsNullOrWhiteSpace(steamId64)
           && steamId64.Length == 17
           && steamId64.All(char.IsDigit);

    private static string NormalizeSteamId64(string? steamId64)
    {
        var value = steamId64?.Trim() ?? string.Empty;
        if (!IsValidSteamId64(value))
            throw new InvalidOperationException("A valid 17-digit SteamID64 is required for account-scoped data.");
        return value;
    }

    private static void UpdateMetadataUnsafe(string steamId64, string personaName)
    {
        try
        {
            var path = Path.Combine(GetAccountDirectory(steamId64), "account.json");
            AccountMetadata metadata;
            if (File.Exists(path))
            {
                try
                {
                    metadata = JsonSerializer.Deserialize<AccountMetadata>(File.ReadAllText(path)) ?? new AccountMetadata();
                }
                catch
                {
                    metadata = new AccountMetadata();
                }
            }
            else
            {
                metadata = new AccountMetadata();
            }

            var now = DateTimeOffset.Now;
            metadata.SteamId64 = steamId64;
            if (!string.IsNullOrWhiteSpace(personaName))
                metadata.PersonaName = personaName;
            if (metadata.FirstSeenAt == default)
                metadata.FirstSeenAt = now;
            metadata.LastSeenAt = now;

            File.WriteAllText(path, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not update account metadata for SteamID64 {steamId64}: {ex.Message}");
        }
    }
}
