using System.Net.Http;

namespace TF2IdleManager;

// App-owned notification: native WinForms tray balloons cannot embed an item image.
// This non-activating toast displays one while preserving keyboard focus on TF2.
public sealed class DropToastForm : Form
{
    private static readonly HttpClient ImageClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly PictureBox _itemImage = new();
    private readonly System.Windows.Forms.Timer _dismissTimer = new();
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | TOOLWINDOW
            return cp;
        }
    }

    public DropToastForm(PendingDropItem item, bool darkMode, Action openTf2)
    {
        var background = darkMode ? Color.FromArgb(30, 37, 47) : Color.FromArgb(245, 248, 251);
        var foreground = darkMode ? Color.WhiteSmoke : Color.FromArgb(30, 35, 43);
        Text = "TF2 Idle Manager";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(430, 158);
        BackColor = background;
        // Location is assigned by MainForm so every alert shares the selected corner
        // and monitor. A standalone caller may set Location before Show().

        var headline = new Label
        {
            Text = item.IsSeparateTimerContainer ? "TF2 container received" : "TF2 regular drop",
            ForeColor = darkMode ? Color.FromArgb(244, 170, 72) : Color.FromArgb(155, 84, 26),
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 11, FontStyle.Bold),
            AutoEllipsis = true,
            Bounds = new Rectangle(110, 16, 274, 27)
        };
        var title = new Label
        {
            Text = item.DisplayName,
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 10, FontStyle.Bold),
            ForeColor = foreground,
            AutoEllipsis = true,
            Bounds = new Rectangle(110, 48, 296, 27)
        };
        var info = new Label
        {
            Text = item.IsSeparateTimerContainer
                ? "Separate-timer container · not a regular weekly drop"
                : "Regular weekly drop · pending acknowledgement",
            ForeColor = foreground,
            AutoEllipsis = true,
            Bounds = new Rectangle(110, 82, 305, 35)
        };
        _itemImage.Bounds = new Rectangle(12, 18, 87, 87);
        _itemImage.SizeMode = PictureBoxSizeMode.Zoom;
        _itemImage.BackColor = darkMode ? Color.FromArgb(18, 25, 33) : Color.White;
        _itemImage.BorderStyle = BorderStyle.FixedSingle;
        // A valid fallback image is shown when a test has no item asset or the API
        // does not supply an image. The toast never invents an item thumbnail.
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "TF2IdleManager.ico");
            using var fallback = new Icon(iconPath);
            _itemImage.Image = fallback.ToBitmap();
        }
        catch { /* item name remains visible if even the bundled icon is missing */ }
        var openButton = new ThemedButton
        {
            Text = "Open TF2", Bounds = new Rectangle(285, 120, 125, 32),
            DarkAppearance = darkMode, FlatStyle = FlatStyle.Flat,
            BackColor = darkMode ? Color.FromArgb(38, 104, 169) : Color.FromArgb(50, 119, 192),
            ForeColor = Color.White, UseVisualStyleBackColor = false
        };
        openButton.FlatAppearance.BorderSize = 1;
        openButton.FlatAppearance.BorderColor = darkMode ? Color.FromArgb(95, 164, 224) : Color.FromArgb(31, 91, 155);
        openButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(49, 128, 198);
        openButton.Click += (_, _) => { openTf2(); Close(); };
        var dismiss = new ThemedButton { Text = "×", Bounds = new Rectangle(390, 9, 30, 26), FlatStyle = FlatStyle.Flat, DarkAppearance = darkMode };
        dismiss.FlatAppearance.BorderSize = 0;
        dismiss.ForeColor = foreground;
        dismiss.BackColor = background;
        dismiss.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { _itemImage, headline, title, info, openButton, dismiss });
        _dismissTimer.Interval = 14000;
        _dismissTimer.Tick += (_, _) => Close();
        _dismissTimer.Start();
        Shown += async (_, _) => await LoadItemImageAsync(item.ImageUrl);
        FormClosed += (_, _) => { _dismissTimer.Stop(); _dismissTimer.Dispose(); _itemImage.Image?.Dispose(); };
    }

    private async Task LoadItemImageAsync(string? url)
    {
        try
        {
            var normalized = SteamItemImageUrl.Normalize(url);
            if (normalized is null) return;
            var uri = new Uri(normalized);
            using var response = await ImageClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 2 * 1024 * 1024)
                return;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 2 * 1024 * 1024 || IsDisposed)
                return;
            using var ms = new MemoryStream(bytes);
            using var image = Image.FromStream(ms);
            if (!IsDisposed)
            {
                var previous = _itemImage.Image;
                _itemImage.Image = new Bitmap(image);
                previous?.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Drop thumbnail unavailable, title-only toast used: {ex.GetType().Name}");
        }
    }
}
