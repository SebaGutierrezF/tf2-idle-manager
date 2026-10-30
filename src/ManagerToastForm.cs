namespace TF2IdleManager;

// Manager-owned informational toast, so API/watchdog status alerts can be positioned
// consistently with drop/image toasts. Never steals keyboard focus from TF2.
public sealed class ManagerToastForm : Form
{
    private readonly System.Windows.Forms.Timer _dismissTimer = new();
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    public ManagerToastForm(string heading, string message, bool darkMode)
    {
        Text = "TF2 Idle Manager";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(440, 177);
        var background = darkMode ? Color.FromArgb(30, 37, 47) : Color.FromArgb(245, 248, 251);
        var foreground = darkMode ? Color.WhiteSmoke : Color.FromArgb(30, 35, 43);
        BackColor = background;

        var picture = new PictureBox
        {
            Bounds = new Rectangle(14, 18, 66, 66),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = darkMode ? Color.FromArgb(18, 25, 33) : Color.White
        };
        try
        {
            var assetPath = Path.Combine(AppContext.BaseDirectory, "assets", "TF2IdleManager.ico");
            using var appIcon = new Icon(assetPath);
            picture.Image = appIcon.ToBitmap();
        }
        catch { /* The notification text remains functional without an icon. */ }

        var headline = new Label
        {
            Text = heading,
            ForeColor = darkMode ? Color.FromArgb(244, 170, 72) : Color.FromArgb(155, 84, 26),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            Bounds = new Rectangle(93, 15, 304, 30),
            AutoEllipsis = true
        };
        var content = new Label
        {
            Text = message,
            ForeColor = foreground,
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            Bounds = new Rectangle(93, 48, 332, 78),
            AutoEllipsis = true
        };
        var closeButton = new ThemedButton
        {
            Text = "Dismiss", Bounds = new Rectangle(305, 136, 118, 31),
            DarkAppearance = darkMode, FlatStyle = FlatStyle.Flat,
            BackColor = darkMode ? Color.FromArgb(38, 104, 169) : Color.FromArgb(50, 119, 192),
            ForeColor = Color.White, UseVisualStyleBackColor = false
        };
        closeButton.FlatAppearance.BorderColor = Color.FromArgb(95, 164, 224);
        closeButton.Click += (_, _) => Close();
        var dismiss = new ThemedButton
        {
            Text = "×", Bounds = new Rectangle(402, 7, 30, 26),
            FlatStyle = FlatStyle.Flat, DarkAppearance = darkMode,
            ForeColor = foreground, BackColor = background
        };
        dismiss.FlatAppearance.BorderSize = 0;
        dismiss.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { picture, headline, content, closeButton, dismiss });
        _dismissTimer.Interval = 14000;
        _dismissTimer.Tick += (_, _) => Close();
        _dismissTimer.Start();
        FormClosed += (_, _) =>
        {
            _dismissTimer.Stop();
            _dismissTimer.Dispose();
            picture.Image?.Dispose();
        };
    }
}
