using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace TF2IdleManager;

public static partial class SteamLocator
{
    public static SteamDetection Detect()
    {
        var steamDir = FindSteamDirectory();
        if (steamDir is null)
            return new SteamDetection(null, null, null, null, null, null, null);

        var steamExe = Path.Combine(steamDir, "steam.exe");
        if (!File.Exists(steamExe))
            steamExe = null;

        var tf2Dir = FindTf2Directory(steamDir);
        var cfgDir = tf2Dir is null ? null : Path.Combine(tf2Dir, "tf", "cfg");
        var consoleLog = tf2Dir is null ? null : Path.Combine(tf2Dir, "tf", "console.log");
        var user = FindMostRecentSteamUser(steamDir);

        return new SteamDetection(
            steamDir,
            steamExe,
            tf2Dir,
            cfgDir,
            consoleLog,
            user?.PersonaName,
            user?.SteamId64
        );
    }

    private static string? FindSteamDirectory()
    {
        var candidates = new List<string?>();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            candidates.Add(key?.GetValue("SteamPath") as string);
            candidates.Add(Path.GetDirectoryName(key?.GetValue("SteamExe") as string));
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            candidates.Add(key?.GetValue("InstallPath") as string);
        }
        catch { }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        foreach (var raw in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            try
            {
                var normalized = Path.GetFullPath(raw!);
                if (File.Exists(Path.Combine(normalized, "steam.exe")))
                    return normalized;
            }
            catch { }
        }

        return null;
    }

    private static string? FindTf2Directory(string steamDir)
    {
        foreach (var library in EnumerateSteamLibraries(steamDir))
        {
            var steamApps = Path.Combine(library, "steamapps");
            var manifest = Path.Combine(steamApps, "appmanifest_440.acf");
            if (!File.Exists(manifest))
                continue;

            var installDir = "Team Fortress 2";
            try
            {
                var text = File.ReadAllText(manifest);
                var match = InstallDirRegex().Match(text);
                if (match.Success)
                    installDir = match.Groups[1].Value;
            }
            catch { }

            var candidate = Path.Combine(steamApps, "common", installDir);
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static SteamUserIdentity? FindMostRecentSteamUser(string steamDir)
    {
        var path = Path.Combine(steamDir, "config", "loginusers.vdf");
        if (!File.Exists(path))
            return null;

        try
        {
            var text = File.ReadAllText(path);
            SteamUserIdentity? fallback = null;

            foreach (Match block in LoginUserBlockRegex().Matches(text))
            {
                var steamId = block.Groups["id"].Value.Trim();
                var body = block.Groups["body"].Value;
                var personaMatch = PersonaNameRegex().Match(body);
                var persona = personaMatch.Success ? personaMatch.Groups[1].Value.Trim() : string.Empty;

                if (steamId.Length != 17 || !steamId.All(char.IsDigit))
                    continue;

                var identity = new SteamUserIdentity(persona, steamId);
                fallback ??= identity;

                var recentMatch = MostRecentRegex().Match(body);
                if (recentMatch.Success && recentMatch.Groups[1].Value == "1")
                    return identity;
            }

            return fallback;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not read Steam loginusers.vdf: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamDir)
    {
        var libraries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (seen.Add(steamDir))
            libraries.Add(steamDir);

        var vdf = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return libraries;

        string text;
        try
        {
            text = File.ReadAllText(vdf);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not read Steam libraryfolders.vdf: {ex.Message}");
            return libraries;
        }

        foreach (Match match in LibraryPathRegex().Matches(text))
        {
            var raw = match.Groups[1].Value.Replace("\\\\", "\\");
            try
            {
                var full = Path.GetFullPath(raw);
                if (Directory.Exists(full) && seen.Add(full))
                    libraries.Add(full);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Ignoring invalid Steam library path '{raw}': {ex.Message}");
            }
        }

        return libraries;
    }

    private sealed record SteamUserIdentity(string PersonaName, string SteamId64);

    [GeneratedRegex("\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("\\\"installdir\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex("\\\"(?<id>\\d{17})\\\"\\s*\\{(?<body>.*?)\\n\\s*\\}", RegexOptions.Singleline)]
    private static partial Regex LoginUserBlockRegex();

    [GeneratedRegex("\\\"PersonaName\\\"\\s*\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex PersonaNameRegex();

    [GeneratedRegex("\\\"MostRecent\\\"\\s*\\\"([01])\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex MostRecentRegex();
}
