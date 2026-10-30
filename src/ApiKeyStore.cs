using System.Security.Cryptography;
using System.Text;

namespace TF2IdleManager;

public static class ApiKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TF2IdleManager.SteamWebApiKey.v1");
    private static string KeyPath => Path.Combine(AppLogger.AppDataDirectory, "steam-web-api-key.dat");

    private static string SecondaryKeyPath => Path.Combine(AppLogger.AppDataDirectory, "steam-web-api-key-secondary.dat");

    public static string? LoadSecondary() => LoadProtected(SecondaryKeyPath);
    public static void SaveSecondary(string? apiKey) => SaveProtected(SecondaryKeyPath, apiKey);

    public static string? Load() => LoadProtected(KeyPath);

    private static string? LoadProtected(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var encrypted = File.ReadAllBytes(path);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var value = Encoding.UTF8.GetString(clear).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not load the protected Steam Web API key: {ex.Message}");
            return null;
        }
    }

    public static void Save(string? apiKey) => SaveProtected(KeyPath, apiKey);

    private static void SaveProtected(string path, string? apiKey)
    {
        try
        {
            Directory.CreateDirectory(AppLogger.AppDataDirectory);
            var value = apiKey?.Trim() ?? string.Empty;

            if (value.Length == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var clear = Encoding.UTF8.GetBytes(value);
            var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not save the protected Steam Web API key: {ex.Message}");
            throw;
        }
    }
}
