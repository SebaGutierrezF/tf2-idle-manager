namespace TF2IdleManager;

public static class AppLogger
{
    private static readonly object Sync = new();
    private const int RetentionDays = 14;

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TF2IdleManager"
    );

    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");

    public static string CurrentLogPath => Path.Combine(
        LogDirectory,
        $"tf2-idle-manager-{DateTime.Now:yyyy-MM-dd}.log"
    );

    public static event Action<string>? LineWritten;

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            CleanupOldLogs();
            Info($"TF2 Idle Manager v1.0.0 starting | OS={Environment.OSVersion} | 64bit={Environment.Is64BitProcess}");
        }
        catch
        {
            // Logging must never prevent startup.
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        var text = exception is null
            ? message
            : $"{message} | {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{level}] {message}";
            lock (Sync)
            {
                File.AppendAllText(CurrentLogPath, line + Environment.NewLine);
            }
            LineWritten?.Invoke(line);
        }
        catch
        {
            // Never throw from logging.
        }
    }

    private static void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "tf2-idle-manager-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // Best effort per file.
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
