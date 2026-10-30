using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using TF2IdleManager;

// Dependency-free offline regression suite. No Steam requests, no TF2 process,
// and NO reads/writes to the user's %LOCALAPPDATA%\TF2IdleManager.
var tests = new List<(string Name, Action Run)>();
void Add(string name, Action run) => tests.Add((name, run));
void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}

Add("Class registry contains 9 classes + Random", () =>
{
    Check(Tf2Classes.Names.Count == 10, "Expected 10 class choices");
    Check(Tf2Classes.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10, "Duplicate classes");
    Check(Tf2Classes.Normalize("heavy") == "Heavy", "Case-insensitive class normalization");
    Check(Tf2Classes.Normalize("unknown") == "Random", "Unknown classes fail safe to Random");
});
Add("Heavy and Pyro use original TF2 class-menu keys", () =>
{
    Check(Tf2Classes.Key("Heavy") == 0x35 && Tf2Classes.Cfg("Heavy") == "heavyweapons.cfg", "Heavy mapping changed");
    Check(Tf2Classes.Key("Pyro") == 0x33 && Tf2Classes.Cfg("Pyro") == "pyro.cfg", "Pyro mapping changed");
});
Add("Container classification excludes supply crates", () =>
{
    Check(Tf2DropClassifier.IsSeparateTimerContainer("supply_crate", null, "Some mystery item"), "item_class failed");
    Check(Tf2DropClassifier.IsSeparateTimerContainer(null, "supply_crate", "Mystery"), "craft_class failed");
    Check(Tf2DropClassifier.IsSeparateTimerContainer(null, null, "Haunted Hoard Case"), "Case fallback failed");
    Check(!Tf2DropClassifier.IsSeparateTimerContainer(null, null, "Killing Gloves of Boxing"), "Regular item misclassified");
});
Add("Historical and pending categories stay distinct", () =>
{
    var item = new PendingDropItem(1, 123, 0, 0, "Mann Co. Supply Crate", "supply_crate");
    Check(item.IsSeparateTimerContainer && !item.CountsTowardWeeklyMetrics, "Crate counted as regular");
    var regular = new PendingDropItem(2, 99, 0, 0, "The Detonator");
    Check(regular.CountsTowardWeeklyMetrics, "Regular item not counted");
    var legacy = new DropHistoryEntry { DisplayName = "Haunted Hoard Case" };
    Check(!legacy.CountsTowardWeeklyMetrics, "Legacy container not excluded");
});
Add("Four corners and stacked placement are deterministic", () =>
{
    var area = new Rectangle(100, 50, 1000, 800);
    var size = new Size(200, 100);
    Check(ToastPlacement.Position(area, size, "TopLeft") == new Point(116, 66), "TopLeft wrong");
    Check(ToastPlacement.Position(area, size, "TopRight") == new Point(884, 66), "TopRight wrong");
    Check(ToastPlacement.Position(area, size, "BottomLeft") == new Point(116, 734), "BottomLeft wrong");
    Check(ToastPlacement.Position(area, size, "BottomRight") == new Point(884, 734), "BottomRight wrong");
    Check(ToastPlacement.Position(area, size, "BottomLeft", 40) == new Point(116, 694), "Toast stacking wrong");
    Check(ToastPlacement.Normalize("unexpected") == "BottomLeft", "Corner fallback wrong");
});
Add("Server URL normalization avoids accidental command text", () =>
{
    Check(Tf2SessionManager.NormalizeServerForSettings("connect 127.0.0.1:27015") == "127.0.0.1:27015", "connect normalization failed");
    Check(Tf2SessionManager.NormalizeServerForSettings("steam://connect/example.com:27015") == "example.com:27015", "Steam URL normalization failed");
    Check(Tf2SessionManager.NormalizeServerForSettings("bad server;quit") is null, "Unsafe server accepted");
});
Add("Win64 SendInput interop uses complete 40-byte INPUT union", () =>
{
    var t = typeof(Tf2SessionManager).GetNestedType("INPUT", BindingFlags.NonPublic);
    Check(t is not null, "INPUT type unavailable");
    Check(Marshal.SizeOf(t!) == 40, $"INPUT is {Marshal.SizeOf(t!)} bytes (expected 40)");
});
Add("CSV escapes spreadsheet formulas, doubles quotes, uses BOM", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "tf2im-test-" + Guid.NewGuid().ToString("N") + ".csv");
    try
    {
        CsvExportService.ExportDrops(path, new [] { new DropHistoryEntry
        {
            SteamId64 = "00000000000000000", ItemId = 1, DefIndex = 2,
            DisplayName = "=HYPERLINK(\"bad\")", DetectedAt = DateTimeOffset.UtcNow,
            Server = "@unsafe", DropType = Tf2DropClassifier.RegularDropType
        }});
        var bytes = File.ReadAllBytes(path);
        Check(bytes.Length > 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf, "CSV BOM missing");
        var data = File.ReadAllText(path, Encoding.UTF8);
        Check(data.Contains("\"'=HYPERLINK(\"\"bad\"\")\""), "CSV formula injection protection failed");
        Check(data.Contains("\"'@unsafe\""), "Server formula protection failed");
        Check(!data.Contains("steam-web-api-key"), "CSV unexpectedly contains key path");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
});
Add("Account paths remain separated by SteamID64 without writing user data", () =>
{
    var first = AccountDataContext.GetAccountDirectory("00000000000000000");
    var second = AccountDataContext.GetAccountDirectory("11111111111111111");
    Check(!string.Equals(first, second, StringComparison.OrdinalIgnoreCase), "Account directories overlap");
    Check(first.EndsWith("00000000000000000") && second.EndsWith("11111111111111111"), "Wrong account suffix");
});
Add("Session CSV includes bounded duration without credentials", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "tf2im-session-test-" + Guid.NewGuid().ToString("N") + ".csv");
    try
    {
        var start = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        CsvExportService.ExportSessions(path, new[] { new ManagedSessionRecord {
            SteamId64 = "00000000000000000", SessionId = "offline-test", StartedAt = start,
            LastUpdatedAt = start.AddMinutes(30), EndedAt = start.AddMinutes(30),
            EligibleSeconds = 1500, ApiOutageSeconds = 120, State = "Completed"
        } }, Array.Empty<DropHistoryEntry>());
        var text = File.ReadAllText(path);
        Check(text.Contains("\"30.0\""), "Session wall duration incorrect");
        Check(text.Contains("\"25.0\""), "Session eligible duration incorrect");
        Check(text.Contains("\"2.0\""), "API outage duration incorrect");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
});

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"[PASS] {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"[FAIL] {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"Offline tests: {tests.Count - failed}/{tests.Count} passed.");
return failed == 0 ? 0 : 1;
