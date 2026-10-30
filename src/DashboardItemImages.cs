using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace TF2IdleManager;

// Best-effort artwork for recent drops. A small bounded disk cache makes images
// available even when Steam's schema endpoint is intermittently unavailable.
// A failed picture must never affect drop alerts or game controls.
internal static class DashboardItemImages
{
    private const int MaxImageBytes = 768 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    private static readonly SemaphoreSlim DownloadGate = new(2, 2);
    private static string CacheFolder => Path.Combine(AppLogger.AppDataDirectory, "item-artwork");

    public static async Task FillAsync(PictureBox picture, Label fallback, string url)
    {
        var safeUrl = SteamItemImageUrl.Normalize(url);
        if (safeUrl is null) return;
        try
        {
            var bytes = await GetBytesAsync(safeUrl);
            if (bytes is null || picture.IsDisposed) return;
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            var clone = new Bitmap(image);
            if (picture.IsDisposed) { clone.Dispose(); return; }
            if (picture.InvokeRequired)
            {
                try { picture.BeginInvoke((Action)(() => AssignImage(picture, fallback, clone))); }
                catch (InvalidOperationException) { clone.Dispose(); }
            }
            else AssignImage(picture, fallback, clone);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Dashboard artwork unavailable: {ex.GetType().Name}");
        }
    }

    private static async Task<byte[]?> GetBytesAsync(string safeUrl)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(safeUrl)));
        var path = Path.Combine(CacheFolder, hash + ".img");
        if (File.Exists(path))
        {
            try
            {
                var disk = await File.ReadAllBytesAsync(path);
                if (disk.Length > 0 && disk.Length <= MaxImageBytes) return disk;
            }
            catch (IOException) { /* Try the network when disk is busy. */ }
        }
        await DownloadGate.WaitAsync();
        try
        {
            // A second request may have just populated the same cache file.
            if (File.Exists(path))
            {
                try
                {
                    var disk = await File.ReadAllBytesAsync(path);
                    if (disk.Length > 0 && disk.Length <= MaxImageBytes) return disk;
                }
                catch (IOException) { }
            }
            using var response = await Client.GetAsync(safeUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes ||
                response.Content.Headers.ContentType?.MediaType is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif"))
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 20 || bytes.Length > MaxImageBytes) return null;
            try
            {
                Directory.CreateDirectory(CacheFolder);
                await File.WriteAllBytesAsync(path, bytes);
            }
            catch (IOException) { /* Cache write is optional. */ }
            return bytes;
        }
        finally { DownloadGate.Release(); }
    }

    private static void AssignImage(PictureBox picture, Label fallback, Bitmap bitmap)
    {
        if (picture.IsDisposed) { bitmap.Dispose(); return; }
        var previous = picture.Image;
        picture.Image = bitmap;
        fallback.Visible = false;
        previous?.Dispose();
    }
}
