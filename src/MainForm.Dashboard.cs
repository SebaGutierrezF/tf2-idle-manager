namespace TF2IdleManager;

// The dashboard is presentation-only. The existing MainForm session, API, account,
// watchdog and notification handlers are deliberately reused without changing them.
public sealed partial class MainForm
{
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, Panel> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Label _pageHeading = new();
    private readonly Label _pageSubheading = new();
    private readonly Label _accountBanner = new();
    private readonly Label _dashboardTime = new();
    private readonly Label _dashboardDrops = new();
    private readonly Label _dashboardContainers = new();
    private readonly Label _dashboardApi = new();
    private readonly Label _dashboardEligible = new();
    private readonly Label _dashboardRemaining = new();
    private readonly Label _dashboardExpected = new();
    private readonly Label _dashboardAccount = new();
    private readonly Label _dashboardMonitor = new();
    private readonly Label _dashboardWeeklyEligible = new();
    private readonly Label _dashboardWeeklySummary = new();
    private readonly Label _dashboardWeeklyDetail = new();
    private readonly Panel _weeklyProgress = new() { Height = 12, Dock = DockStyle.Fill };
    private double _weeklyProgressRatio;
    private readonly Tf2ItemNameResolver _historyArtworkResolver = new();
    private readonly CancellationTokenSource _historyArtworkCancellation = new();
    private bool _historyArtworkLoading;
    private DateTimeOffset _nextArtworkWarmAt = DateTimeOffset.UtcNow.AddSeconds(20);
    private Panel? _dashboardScroll;
    private int _dashboardSessionScrollY;
    private readonly Label _accountsContent = new();
    private readonly Label _serversContent = new();
    private readonly Label _weeklyTitle = new();
    private readonly Label _weeklyRegularMetric = new();
    private readonly Label _weeklyContainerMetric = new();
    private readonly Label _weeklyTimeMetric = new();
    private readonly Label _weeklyComparison = new();
    private readonly ComboBox _weeklyWeekSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _weeklyTrendGrid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
        MultiSelect = false, Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly FlowLayoutPanel _weeklyItemCards = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false,
        FlowDirection = FlowDirection.TopDown, Padding = new Padding(5), Tag = "card"
    };
    private DateTimeOffset? _weeklySelectedStart;
    private string? _weeklyViewSteamId;
    private DateTimeOffset _nextWeeklyRefresh = DateTimeOffset.MinValue;
    private bool _weeklyChangingSelection;
    private readonly Label _apiPrimaryState = new();
    private readonly Label _apiSecondaryState = new();
    private DateTimeOffset _apiStatusRefreshAfter = DateTimeOffset.MinValue;
    private bool _historySessionsMode;
    private readonly Button _historySessionsButton = new ThemedButton();
    private readonly FlowLayoutPanel _historyVisual = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, Padding = new Padding(6), Tag = "card"
    };
    private readonly ComboBox _historyFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _historyModeButton = new ThemedButton();
    private bool _historyVisualMode = true;
    private readonly DataGridView _historyGrid = new() { ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly FlowLayoutPanel _recentDrops = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(2) };
    private readonly Button _quickTheme = new ThemedButton();
    private DateTimeOffset _recentRefreshed = DateTimeOffset.MinValue;
    private string? _recentSteamId;
    private int _recentFingerprint;
    private bool _dashboardThemeApplying;
    private string _currentPage = "Dashboard";

    private static readonly Color DarkCanvas = Color.FromArgb(15, 22, 30);
    private static readonly Color DarkSide = Color.FromArgb(18, 27, 37);
    private static readonly Color DarkCard = Color.FromArgb(29, 39, 51);
    private static readonly Color DarkField = Color.FromArgb(40, 52, 66);
    private static readonly Color DarkLine = Color.FromArgb(62, 77, 94);
    private static readonly Color DarkMuted = Color.FromArgb(162, 179, 198);
    private static readonly Color Accent = Color.FromArgb(60, 134, 225);
    private static readonly Color Green = Color.FromArgb(92, 203, 127);
    private static readonly Color Orange = Color.FromArgb(243, 155, 64);

    private static Label UiLabel(string text, float size = 9.5f, bool strong = false, string tag = "normal") => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", size, strong ? FontStyle.Bold : FontStyle.Regular),
        AutoEllipsis = true,
        Tag = tag,
        Margin = new Padding(3)
    };

    private static TableLayoutPanel Stack(int columns = 1) => new()
    {
        ColumnCount = columns,
        Dock = DockStyle.Fill,
        Padding = new Padding(0),
        Margin = new Padding(0)
    };

    private static void FixedRows(TableLayoutPanel table, params int[] heights)
    {
        table.RowCount = heights.Length;
        foreach (var h in heights)
            table.RowStyles.Add(new RowStyle(h < 0 ? SizeType.Percent : SizeType.Absolute, h < 0 ? 100 : h));
    }

    private static Panel Card(string heading, Control body, int height)
    {
        var panel = new Panel { Height = height, Dock = DockStyle.Top, Tag = "card", Padding = new Padding(13), Margin = new Padding(0, 0, 0, 12) };
        var layout = Stack();
        FixedRows(layout, 32, -1);
        layout.Controls.Add(UiLabel(heading, 11.0f, true), 0, 0);
        body.Dock = DockStyle.Fill;
        layout.Controls.Add(body, 0, 1);
        panel.Controls.Add(layout);
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(panel.FindForm() is MainForm f && f._settings.DarkMode ? DarkLine : Color.FromArgb(215, 221, 229));
            if (panel.ClientSize.Width > 2 && panel.ClientSize.Height > 2)
                e.Graphics.DrawRectangle(pen, 0, 0, panel.ClientSize.Width - 1, panel.ClientSize.Height - 1);
        };
        return panel;
    }

    private static Button UiButton(string text, string type = "button") => new ThemedButton()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Tag = type,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 9.3f, FontStyle.Bold),
        Margin = new Padding(3, 4, 3, 4),
        Cursor = Cursors.Hand
    };

    private static void Put(TableLayoutPanel table, Control control, int col, int row, int colSpan = 1)
    {
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, col, row);
        if (colSpan > 1) table.SetColumnSpan(control, colSpan);
    }

    private void BuildUi()
    {
        Text = "TF2 Idle Manager v1.0.0";
        var shell = Stack(2);
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);
        Disposed += (_, _) =>
        {
            _historyArtworkCancellation.Cancel();
            _historyArtworkResolver.Dispose();
            _historyArtworkCancellation.Dispose();
        };
        _weeklyProgress.Paint += (_, e) =>
        {
            var bounds = _weeklyProgress.ClientRectangle;
            if (bounds.Width <= 1 || bounds.Height <= 1) return;
            using var bg = new SolidBrush(_settings.DarkMode ? DarkField : Color.FromArgb(213, 223, 235));
            using var fg = new SolidBrush(Accent);
            e.Graphics.FillRectangle(bg, bounds);
            var width = Math.Clamp((int)Math.Round(bounds.Width * _weeklyProgressRatio), 0, bounds.Width);
            if (width > 0) e.Graphics.FillRectangle(fg, new Rectangle(0, 0, width, bounds.Height));
        };

        var sidebar = Stack();
        sidebar.Tag = "sidebar";
        sidebar.Padding = new Padding(10, 13, 10, 10);
        FixedRows(sidebar, 82, -1, 120);
        var brand = Stack(2);
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(1, 7, 6, 7) };
        try { logo.Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "assets", "TF2IdleManager.png")); }
        catch { try { logo.Image = Icon?.ToBitmap(); } catch { /* no icon available */ } }
        var brandName = UiLabel("TF2 Idle\nManager", 14, true);
        Put(brand, logo, 0, 0);
        Put(brand, brandName, 1, 0);
        Put(sidebar, brand, 0, 0);
        // FlowLayoutPanel + Dock=Fill on buttons collapsed navigation in the first dashboard.
        // A fixed-row table makes every destination visible independent of scrolling/DPI.
        var navigation = Stack();
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        FixedRows(navigation, 47, 47, 47, 47, 47, 47, 47, 47);
        var navigationItems = new (string Name, string Symbol)[]
        {
            ("Dashboard", "⌂"), ("Session", "▶"), ("Weekly Details", "▥"),
            ("History", "◷"), ("Accounts", "♙"), ("Servers", "▣"),
            ("Settings", "⚙"), ("About", "ⓘ")
        };
        for (var i = 0; i < navigationItems.Length; i++)
        {
            var (name, symbol) = navigationItems[i];
            var button = UiButton($"  {symbol}   {name}", "nav");
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Margin = new Padding(2, 2, 2, 3);
            button.Click += (_, _) => Navigate(name);
            _navButtons[name] = button;
            Put(navigation, button, 0, i);
        }
        Put(sidebar, navigation, 0, 1);
        var bottom = Stack();
        FixedRows(bottom, 40, 36, 36);
        _quickTheme.Text = "Toggle light / dark";
        _quickTheme.Tag = "button";
        _quickTheme.Dock = DockStyle.Fill;
        _quickTheme.Click += (_, _) =>
        {
            _settings.DarkMode = !_settings.DarkMode;
            SettingsStore.Save(_settings);
            ThemeHelper.Apply(this, _settings.DarkMode);
            ApplyDashboardTheme();
        };
        Put(bottom, _quickTheme, 0, 0);
        Put(bottom, UiLabel("v1.0.0  •  Windows", 8.5f, false, "muted"), 0, 1);
        Put(bottom, UiLabel("Steam / TF2 monitor", 8.5f, false, "muted"), 0, 2);
        Put(sidebar, bottom, 0, 2);
        Put(shell, sidebar, 0, 0);

        var workspace = Stack();
        FixedRows(workspace, 74, -1);
        var pageHeader = Stack(2);
        pageHeader.Padding = new Padding(18, 10, 18, 8);
        pageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        pageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        var titleStack = Stack();
        FixedRows(titleStack, 33, 25);
        _pageHeading.Font = new Font("Segoe UI", 20, FontStyle.Bold);
        _pageHeading.Dock = DockStyle.Fill;
        _pageHeading.Tag = "normal";
        _pageSubheading.Font = new Font("Segoe UI", 9.1f);
        _pageSubheading.Dock = DockStyle.Fill;
        _pageSubheading.Tag = "muted";
        Put(titleStack, _pageHeading, 0, 0);
        Put(titleStack, _pageSubheading, 0, 1);
        Put(pageHeader, titleStack, 0, 0);
        _accountBanner.TextAlign = ContentAlignment.MiddleRight;
        _accountBanner.Dock = DockStyle.Fill;
        _accountBanner.Tag = "muted";
        _accountBanner.AutoEllipsis = true;
        Put(pageHeader, _accountBanner, 1, 0);
        Put(workspace, pageHeader, 0, 0);
        _pageHost.Padding = new Padding(16, 2, 16, 12);
        Put(workspace, _pageHost, 0, 1);
        Put(shell, workspace, 1, 0);

        BuildDashboardPage();
        BuildHistoryPage();
        BuildWeeklyPage();
        BuildAccountsPage();
        BuildServersPage();
        Navigate("Dashboard");
    }

    private void AddPage(string name, Panel page)
    {
        page.Visible = false;
        page.Dock = DockStyle.Fill;
        _pageHost.Controls.Add(page);
        _pages.Add(name, page);
    }

    private void BuildDashboardPage()
    {
        var page = new Panel();
        var columns = Stack(2);
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        Put(page, columns);

        var left = new Panel { AutoScroll = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 10, 0) };
        var leftContent = Stack();
        leftContent.Dock = DockStyle.Top;
        leftContent.AutoSize = true;
        leftContent.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var heights = new[] { 138, 232, 160, 162, 233, 254 };
        FixedRows(leftContent, heights);
        left.Resize += (_, _) =>
        {
            // Keep the card stack inside the scroll viewport; no horizontal spill.
            leftContent.Width = Math.Max(1, left.ClientSize.Width - left.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
        };
        Put(leftContent, CreateDetectionCard(), 0, 0);
        Put(leftContent, CreateServerCard(), 0, 1);
        Put(leftContent, CreateControlCard(), 0, 2);
        Put(leftContent, CreateSessionCard(), 0, 3);
        Put(leftContent, CreateWeeklyCard(), 0, 4);
        Put(leftContent, CreateActivityCard(), 0, 5);
        leftContent.Width = 720;
        left.Controls.Add(leftContent);
        _dashboardScroll = left;
        _dashboardSessionScrollY = heights.Take(3).Sum();
        Put(columns, left, 0, 0);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 0) };
        var rightStack = Stack();
        FixedRows(rightStack, -1, 310);
        _recentDrops.Resize += (_, _) =>
        {
            var width = Math.Max(250, _recentDrops.ClientSize.Width - 30);
            foreach (Control row in _recentDrops.Controls)
            {
                if (row is not Panel) continue;
                row.Width = width;
                foreach (Control child in row.Controls)
                {
                    if (child is Label label && !ReferenceEquals(label, null))
                    {
                        label.Width = Math.Max(125, width - 92);
                    }
                }
            }
        };
        var recentCard = Card("Recent drops · this TF2 week", _recentDrops, 350);
        recentCard.Dock = DockStyle.Fill;
        recentCard.Margin = new Padding(0, 0, 0, 12);
        Put(rightStack, recentCard, 0, 0);
        var monitor = Stack();
        FixedRows(monitor, 30, 28, 22, 22, 32, 34, 32, 32);
        _dashboardMonitor.Tag = "good";
        _dashboardMonitor.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _dashboardAccount.Tag = "muted";
        Put(monitor, _dashboardMonitor, 0, 0);
        Put(monitor, _dashboardAccount, 0, 1);
        _apiPrimaryState.Tag = "muted";
        _apiSecondaryState.Tag = "muted";
        _apiPrimaryState.Font = new Font("Segoe UI", 8.1f);
        _apiSecondaryState.Font = new Font("Segoe UI", 8.1f);
        Put(monitor, _apiPrimaryState, 0, 2);
        Put(monitor, _apiSecondaryState, 0, 3);
        _apiKeyBox.UseSystemPasswordChar = true;
        _apiKeyBox.PlaceholderText = "Primary Steam API key";
        Put(monitor, _apiKeyBox, 0, 4);
        var actions = Stack(2);
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        _saveApiKeyButton.Text = "Save key";
        _saveApiKeyButton.Tag = "button";
        _checkApiNowButton.Text = "Check now";
        _checkApiNowButton.Tag = "button";
        Put(actions, _saveApiKeyButton, 0, 0);
        Put(actions, _checkApiNowButton, 1, 0);
        Put(monitor, actions, 0, 5);
        _dropNotificationsCheck.Text = "Monitor item drops";
        Put(monitor, _dropNotificationsCheck, 0, 6);
        _testNotificationButton.Text = "Test image alert";
        _testNotificationButton.Tag = "button";
        Put(monitor, _testNotificationButton, 0, 7);
        Put(rightStack, Card("Drop monitoring / Steam API", monitor, 297), 0, 1);
        right.Controls.Add(rightStack);
        Put(columns, right, 1, 0);
        AddPage("Dashboard", page);
    }

    private static void Put(Panel pageWrapper, Control inner)
    {
        inner.Dock = DockStyle.Fill;
        pageWrapper.Controls.Add(inner);
    }

    private Control CreateDetectionCard()
    {
        var content = Stack(2);
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        FixedRows(content, 38, 38);
        foreach (var label in new[] { _steamStatus, _tf2Status })
        {
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            label.Tag = "normal";
        }
        _detectButton.Text = "Refresh";
        _detectButton.Tag = "button";
        Put(content, _steamStatus, 0, 0);
        Put(content, _tf2Status, 0, 1);
        Put(content, _detectButton, 1, 0);
        content.SetRowSpan(_detectButton, 2);
        return Card("Steam / TF2 connection", content, 126);
    }

    private Control CreateServerCard()
    {
        var content = Stack(4);
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        FixedRows(content, 34, 34, 35, 35);
        Put(content, UiLabel("Server", 9), 0, 0);
        _serverBox.PlaceholderText = "server.example.com:27015";
        Put(content, _serverBox, 1, 0, 3);
        Put(content, UiLabel("Presets", 9), 0, 1);
        Put(content, _savedServerCombo, 1, 1, 3);
        _attackCheck.Text = "+attack";
        _leftCheck.Text = "+left";
        _autoReconnectCheck.Text = "Auto reconnect";
        Put(content, _attackCheck, 1, 2);
        Put(content, _leftCheck, 2, 2);
        Put(content, _autoReconnectCheck, 3, 2);
        Put(content, UiLabel("Extra", 9), 0, 3);
        _extraOptionsBox.PlaceholderText = "Optional TF2 launch parameters";
        Put(content, _extraOptionsBox, 1, 3, 3);
        return Card("Idle server / commands", content, 220);
    }

    private Control CreateControlCard()
    {
        var layout = Stack();
        FixedRows(layout, 39, 55);
        var selection = Stack(4);
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 139));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        Put(selection, UiLabel("Class", 9.5f, true), 0, 0);
        _classCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _classCombo.Items.AddRange(Tf2Classes.Names.Cast<object>().ToArray());
        _classCombo.DropDownWidth = 190;
        Put(selection, _classCombo, 1, 0);
        _minimizeCheck.Text = "Minimize TF2";
        Put(selection, _minimizeCheck, 2, 0);
        _closeOnStopCheck.Text = "Close on STOP";
        Put(selection, _closeOnStopCheck, 3, 0);
        Put(layout, selection, 0, 0);
        var commands = Stack(4);
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        _startButton.Text = "▶  START IDLE";
        _stopButton.Text = "■  STOP";
        _openTf2Button.Text = "Open TF2";
        _startButton.Tag = "start";
        _stopButton.Tag = "stop";
        _openTf2Button.Tag = "button";
        Put(commands, _startButton, 0, 0);
        Put(commands, _stopButton, 1, 0);
        Put(commands, _openTf2Button, 2, 0);
        var preferences = UiButton("Settings");
        preferences.Click += (_, _) => OpenPreferences();
        Put(commands, preferences, 3, 0);
        Put(layout, commands, 0, 1);
        return Card("Idle control · class selection", layout, 148);
    }

    private Control CreateSessionCard()
    {
        var body = Stack();
        FixedRows(body, 36, 61);
        var statuses = Stack(5);
        for (var i = 0; i < 5; i++) statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        var labels = new[] { _sessionStatus, _autoHeavyStatus, _watchdogStatus, _serverStatus, _dropStatus };
        foreach (var label in labels) { label.Tag = "muted"; label.AutoEllipsis = true; label.Font = new Font("Segoe UI", 8.7f); }
        for (var i = 0; i < labels.Length; i++) Put(statuses, labels[i], i, 0);
        Put(body, statuses, 0, 0);
        var metrics = Stack(4);
        for (var i = 0; i < 4; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        Put(metrics, Metric("DURATION", _dashboardTime), 0, 0);
        Put(metrics, Metric("SESSION ELIGIBLE", _dashboardEligible), 1, 0);
        Put(metrics, Metric("SESSION REGULAR", _dashboardDrops), 2, 0);
        Put(metrics, Metric("API OUTAGE", _dashboardApi), 3, 0);
        Put(body, metrics, 0, 1);
        return Card("Current session", body, 149);
    }

    private Control CreateWeeklyCard()
    {
        var body = Stack();
        FixedRows(body, 65, 57, 19);
        var metrics = Stack(4);
        for (var i = 0; i < 4; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        Put(metrics, Metric("TRACKED IDLE / WEEK", _dashboardWeeklyEligible), 0, 0);
        Put(metrics, Metric("CONTAINERS", _dashboardContainers), 1, 0);
        Put(metrics, Metric("DROPS LEFT (EST.)", _dashboardRemaining), 2, 0);
        Put(metrics, Metric("NEXT DROP", _dashboardExpected), 3, 0);
        Put(body, metrics, 0, 0);
        var summary = Stack();
        FixedRows(summary, 28, 28);
        _dashboardWeeklySummary.Tag = "normal";
        _dashboardWeeklyDetail.Tag = "muted";
        _dashboardWeeklySummary.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _dashboardWeeklyDetail.Font = new Font("Segoe UI", 8.7f);
        Put(summary, _dashboardWeeklySummary, 0, 0);
        Put(summary, _dashboardWeeklyDetail, 0, 1);
        Put(body, summary, 0, 1);
        _weeklyProgress.Margin = new Padding(3, 7, 3, 4);
        Put(body, _weeklyProgress, 0, 2);
        return Card("Weekly tracked idle time · ~10h reference (not a cap)", body, 220);
    }

    private static Control Metric(string heading, Label label)
    {
        var block = Stack();
        FixedRows(block, 23, -1);
        label.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        label.Tag = "highlight";
        label.AutoEllipsis = true;
        Put(block, UiLabel(heading, 8.1f, false, "muted"), 0, 0);
        Put(block, label, 0, 1);
        return block;
    }

    private Control CreateActivityCard()
    {
        var body = Stack();
        FixedRows(body, 30, -1);
        var top = Stack(3);
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _sessionStatsStatus.Font = new Font("Segoe UI", 8.5f);
        _sessionStatsStatus.Tag = "muted";
        Put(top, _sessionStatsStatus, 0, 0);
        _openLogsButton.Text = "Open logs";
        _openLogsButton.Tag = "button";
        Put(top, _openLogsButton, 1, 0);
        var history = UiButton("Full history");
        history.Click += (_, _) => Navigate("History");
        Put(top, history, 2, 0);
        Put(body, top, 0, 0);
        _logBox.Font = new Font("Consolas", 9.1f);
        _logBox.ReadOnly = true;
        _logBox.WordWrap = true;
        _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.Tag = "editor";
        Put(body, _logBox, 0, 1);
        return Card("Activity log", body, 240);
    }

    private void BuildHistoryPage()
    {
        var page = new Panel { Padding = new Padding(4) };
        var layout = Stack();
        FixedRows(layout, 60, -1);
        var toolbar = Stack(5);
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 137));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        Put(toolbar, UiLabel("Account-local history · Regular drops and separate-timer containers", 10, false, "muted"), 0, 0);
        _historyFilter.Items.AddRange(new object[] { "All drops", "Regular", "Containers", "Pending" });
        _historyFilter.SelectedIndex = 0;
        _historyFilter.SelectedIndexChanged += (_, _) =>
        {
            if (_currentPage == "History") RefreshAuxiliaryPages();
        };
        _historyModeButton.Text = "Table view";
        _historyModeButton.Tag = "button";
        _historyModeButton.Click += (_, _) =>
        {
            _historyVisualMode = !_historyVisualMode;
            _historyModeButton.Text = _historyVisualMode ? "Table view" : "Visual view";
            RefreshAuxiliaryPages();
        };
        Put(toolbar, _historyFilter, 1, 0);
        Put(toolbar, _historyModeButton, 2, 0);
        _historySessionsButton.Text = "Sessions";
        _historySessionsButton.Tag = "button";
        _historySessionsButton.Click += (_, _) =>
        {
            _historySessionsMode = !_historySessionsMode;
            _historySessionsButton.Text = _historySessionsMode ? "Show drops" : "Sessions";
            _historyFilter.Enabled = !_historySessionsMode;
            _historyModeButton.Enabled = !_historySessionsMode;
            RefreshAuxiliaryPages();
        };
        Put(toolbar, _historySessionsButton, 3, 0);
        var exportHistory = UiButton("Export CSV");
        exportHistory.Click += (_, _) => ExportHistoryCsv();
        Put(toolbar, exportHistory, 4, 0);
        Put(layout, toolbar, 0, 0);
        var content = new Panel { Dock = DockStyle.Fill };
        _historyVisual.Resize += (_, _) =>
        {
            var width = Math.Max(280, _historyVisual.ClientSize.Width - 27);
            foreach (Control control in _historyVisual.Controls)
                if (control is Panel) control.Width = width;
        };
        content.Controls.Add(_historyGrid);
        content.Controls.Add(_historyVisual);
        _historyVisual.BringToFront();
        Put(layout, content, 0, 1);
        page.Controls.Add(layout);
        AddPage("History", page);
    }

    private void ExportHistoryCsv()
    {
        if (!AccountDataContext.HasActiveAccount) { MessageBox.Show(this, "Detect your Steam account first."); return; }
        var rows = DropHistoryStore.GetAllEntries();
        var filtered = (_historyFilter.SelectedItem?.ToString() ?? "All drops") switch
        {
            "Regular" => rows.Where(x => x.CountsTowardWeeklyMetrics).ToList(),
            "Containers" => rows.Where(x => !x.CountsTowardWeeklyMetrics).ToList(),
            "Pending" => rows.Where(x => x.AcknowledgedAt is null).ToList(),
            _ => rows.ToList()
        };
        using var dialog = new SaveFileDialog
        {
            Title = _historySessionsMode ? "Export measured sessions" : "Export TF2 drops",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"tf2-{(_historySessionsMode ? "sessions" : "drops")}-{AccountDataContext.ActiveSteamId64}-{DateTime.Now:yyyyMMdd}.csv",
            AddExtension = true, DefaultExt = "csv", OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (_historySessionsMode) CsvExportService.ExportSessions(dialog.FileName, SessionHistoryStore.ReadAll(), rows);
            else CsvExportService.ExportDrops(dialog.FileName, filtered);
            AppendLog($"CSV export complete: {(_historySessionsMode ? "sessions" : "drops")} | SteamID={AccountDataContext.ActiveSteamId64}");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CSV export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void RefreshHistoryCards(IReadOnlyList<DropHistoryEntry> entries)
    {
        _historyArtworkResolver.ReloadFromDiskIfChanged();
        _historyVisual.SuspendLayout();
        var old = _historyVisual.Controls.Cast<Control>().ToArray();
        _historyVisual.Controls.Clear();
        foreach (var control in old) control.Dispose();
        foreach (var entry in entries.Take(120))
        {
            var row = new Panel
            {
                Width = Math.Max(280, _historyVisual.ClientSize.Width - 27),
                Height = 96, Margin = new Padding(0, 0, 0, 7), Tag = "field"
            };
            var picture = new PictureBox
            {
                Location = new Point(12, 11), Size = new Size(74, 74),
                SizeMode = PictureBoxSizeMode.Zoom, Tag = "editor"
            };
            var fallback = new Label
            {
                Text = entry.CountsTowardWeeklyMetrics ? "✦" : "▣",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Symbol", 26), Tag = "warning"
            };
            picture.Controls.Add(fallback);
            row.Controls.Add(picture);
            var title = UiLabel(entry.DisplayName, 11, true);
            title.Dock = DockStyle.None;
            title.Bounds = new Rectangle(100, 10, 560, 27);
            row.Controls.Add(title);
            var info = UiLabel((entry.CountsTowardWeeklyMetrics ? "REGULAR" : "CONTAINER") +
                $"   ·   {entry.DetectedAt.ToLocalTime():yyyy-MM-dd HH:mm}   ·   #{entry.DefIndex}", 8.8f, false, "muted");
            info.Dock = DockStyle.None;
            info.Bounds = new Rectangle(100, 40, 600, 22);
            row.Controls.Add(info);
            var status = UiLabel(entry.AcknowledgedAt is null ? "● Pending acknowledgement" : "✓ Accepted", 9, true,
                entry.AcknowledgedAt is null ? "warning" : "good");
            status.Dock = DockStyle.None;
            status.Bounds = new Rectangle(100, 67, 460, 22);
            row.Controls.Add(status);
            row.Resize += (_, _) =>
            {
                var width = Math.Max(155, row.ClientSize.Width - 112);
                title.Width = width;
                info.Width = width;
                status.Width = width;
            };
            _historyVisual.Controls.Add(row);
            var url = _historyArtworkResolver.GetCachedImageUrl(entry.DefIndex);
            if (!string.IsNullOrWhiteSpace(url))
                _ = DashboardItemImages.FillAsync(picture, fallback, url);
        }
        if (entries.Count == 0)
        {
            var empty = UiLabel("No recorded drops matching this filter.", 11, false, "muted");
            empty.Size = new Size(430, 75);
            _historyVisual.Controls.Add(empty);
        }
        _historyVisual.ResumeLayout();
    }

    private void BuildWeeklyPage()
    {
        var page = new Panel { Padding = new Padding(4) };
        var layout = Stack();
        FixedRows(layout, 67, 147, 210, -1);

        var toolbar = Stack(4);
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 143));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        _weeklyTitle.Tag = "muted";
        _weeklyTitle.Font = new Font("Segoe UI", 10);
        Put(toolbar, _weeklyTitle, 0, 0);
        _weeklyWeekSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_weeklyChangingSelection || _weeklyWeekSelector.SelectedItem is not WeeklySelection selected) return;
            _weeklySelectedStart = selected.Start;
            if (_currentPage == "Weekly Details") RefreshWeeklyExperience();
        };
        Put(toolbar, _weeklyWeekSelector, 1, 0);
        var originalDetails = UiButton("Detailed report");
        originalDetails.Click += (_, _) => ShowWeeklyDetails();
        Put(toolbar, originalDetails, 2, 0);
        var exportWeek = UiButton("Export CSV");
        exportWeek.Click += (_, _) => ExportSelectedWeekCsv();
        Put(toolbar, exportWeek, 3, 0);
        Put(layout, toolbar, 0, 0);

        var metrics = Stack(4);
        foreach (var _ in Enumerable.Range(0, 4))
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        Put(metrics, Metric("REGULAR DROPS", _weeklyRegularMetric), 0, 0);
        Put(metrics, Metric("CONTAINERS", _weeklyContainerMetric), 1, 0);
        Put(metrics, Metric("TRACKED IDLE", _weeklyTimeMetric), 2, 0);
        var comparison = Stack();
        FixedRows(comparison, 25, -1);
        Put(comparison, UiLabel("VS. PRIOR WEEK", 8.1f, false, "muted"), 0, 0);
        _weeklyComparison.Tag = "muted";
        _weeklyComparison.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        Put(comparison, _weeklyComparison, 0, 1);
        Put(metrics, comparison, 3, 0);
        Put(layout, Card("Selected week · local measurements", metrics, 137), 0, 1);

        _weeklyTrendGrid.Tag = "editor";
        _weeklyTrendGrid.AutoGenerateColumns = true;
        _weeklyTrendGrid.AllowUserToResizeRows = false;
        _weeklyTrendGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _weeklyTrendGrid.Rows.Count) return;
            if (_weeklyTrendGrid.Rows[e.RowIndex].Tag is DateTimeOffset start)
            {
                _weeklySelectedStart = start;
                RefreshWeeklyExperience();
            }
        };
        Put(layout, Card("Week-by-week comparison · double-click to select", _weeklyTrendGrid, 200), 0, 2);

        _weeklyItemCards.Resize += (_, _) =>
        {
            var width = Math.Max(290, _weeklyItemCards.ClientSize.Width - 28);
            foreach (Control control in _weeklyItemCards.Controls)
                if (control is Panel) control.Width = width;
        };
        Put(layout, Card("Items in selected week · regular and containers", _weeklyItemCards, 350), 0, 3);
        page.Controls.Add(layout);
        AddPage("Weekly Details", page);
    }

    private void BuildAccountsPage()
    {
        var page = new Panel { Padding = new Padding(8) };
        var layout = Stack();
        FixedRows(layout, 180, 60, -1);
        _accountsContent.Font = new Font("Segoe UI", 12);
        _accountsContent.TextAlign = ContentAlignment.MiddleLeft;
        Put(layout, _accountsContent, 0, 0);
        var row = Stack(2);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var refresh = UiButton("Detect active Steam account");
        refresh.Click += async (_, _) => await DetectAgainAsync();
        var open = UiButton("Open this account's local folder");
        open.Click += (_, _) =>
        {
            if (AccountDataContext.HasActiveAccount && AccountDataContext.ActiveSteamId64 is string id)
                OpenFolder(AccountDataContext.GetAccountDirectory(id));
        };
        Put(row, refresh, 0, 0);
        Put(row, open, 1, 0);
        Put(layout, row, 0, 1);
        Put(layout, UiLabel("Switch accounts in Steam first, then press Detect. Session/drop history remains isolated by SteamID64.", 10, false, "muted"), 0, 2);
        page.Controls.Add(layout);
        AddPage("Accounts", page);
    }

    private void BuildServersPage()
    {
        var page = new Panel { Padding = new Padding(8) };
        var layout = Stack();
        FixedRows(layout, 190, 65, -1);
        _serversContent.Font = new Font("Segoe UI", 11);
        Put(layout, _serversContent, 0, 0);
        var edit = UiButton("Edit saved servers in Preferences");
        edit.Click += (_, _) => { OpenPreferences(); RefreshAuxiliaryPages(); };
        Put(layout, edit, 0, 1);
        Put(layout, UiLabel("Start/stop the session from the Dashboard. Servers can only be changed when TF2 is not managed by this application.", 10, false, "muted"), 0, 2);
        page.Controls.Add(layout);
        AddPage("Servers", page);
    }

    private void Navigate(string name)
    {
        if (name == "Settings") { OpenPreferences(); RefreshAuxiliaryPages(); return; }
        if (name == "About")
        {
            MessageBox.Show(this, "TF2 Idle Manager v1.0.0\n\nUnofficial Windows utility for TF2 idle server sessions and item-drop alerts.\n\nDrop counts are account-local. Weekly remaining drops are estimates, not Valve counters.", "About TF2 Idle Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var jumpToSession = name == "Session";
        if (jumpToSession) name = "Dashboard";
        if (!_pages.ContainsKey(name)) return;
        _currentPage = name;
        foreach (var pair in _pages) pair.Value.Visible = pair.Key == name;
        _pages[name].BringToFront();
        _pageHeading.Text = name;
        _pageSubheading.Text = name switch
        {
            "Dashboard" => "Live session controls, drop statistics and recent activity",
            "History" => "Account-isolated inventory-drop history",
            "Weekly Details" => "Tracked usage and estimated remaining drops",
            "Accounts" => "Steam account data separated by SteamID64",
            "Servers" => "Saved idle server profiles",
            _ => "TF2 Idle Manager"
        };
        RefreshAuxiliaryPages();
        ApplyDashboardTheme();
        if (name == "Dashboard" && _dashboardScroll is not null)
        {
            var targetY = jumpToSession ? _dashboardSessionScrollY : 0;
            void ScrollDashboard()
            {
                if (_dashboardScroll is { IsDisposed: false } scroll)
                    scroll.AutoScrollPosition = new Point(0, targetY);
            }
            if (IsHandleCreated) BeginInvoke((Action)ScrollDashboard);
            else if (targetY == 0) _dashboardScroll.AutoScrollPosition = Point.Empty;
        }
    }

    private void RefreshAuxiliaryPages()
    {
        if (_pages.Count == 0) return;
        var account = AccountDataContext.HasActiveAccount ? AccountDataContext.DescribeActiveAccount() : "No Steam account active";
        _accountBanner.Text = account;
        _accountsContent.Text = $"ACTIVE STEAM ACCOUNT\n\n{account}\n\nAccount data folder:\n{(AccountDataContext.ActiveSteamId64 is string id ? AccountDataContext.GetAccountDirectory(id) : "Not available")}";
        _serversContent.Text = "Current server: " + (_settings.ServerAddress ?? "(not set)") + "\n\nSaved profiles:\n" + string.Join("\n", (_settings.ServerProfiles ?? new List<string>()).Select(s => "• " + s));
        if (_currentPage == "History")
        {
            var rows = AccountDataContext.HasActiveAccount ? DropHistoryStore.GetAllEntries() : Array.Empty<DropHistoryEntry>();
            var filtered = (_historyFilter.SelectedItem?.ToString() ?? "All drops") switch
            {
                "Regular" => rows.Where(x => x.CountsTowardWeeklyMetrics).ToList(),
                "Containers" => rows.Where(x => !x.CountsTowardWeeklyMetrics).ToList(),
                "Pending" => rows.Where(x => x.AcknowledgedAt is null).ToList(),
                _ => rows.ToList()
            };
            _historyGrid.Visible = !_historyVisualMode && !_historySessionsMode;
            _historyVisual.Visible = _historyVisualMode || _historySessionsMode;
            if (_historySessionsMode)
                RefreshHistoricalSessionCards(rows);
            else if (_historyVisualMode)
                RefreshHistoryCards(filtered);
            else
            {
                _historyGrid.DataSource = filtered.Select(x => new
                {
                    Item = x.DisplayName,
                    Type = x.CountsTowardWeeklyMetrics ? "Regular" : "Container",
                    Detected = x.DetectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    Status = x.AcknowledgedAt is null ? "Pending" : "Accepted",
                    x.ItemId,
                    x.Server
                }).ToList();
                DashboardStyleGrid(_historyGrid, _settings.DarkMode);
            }
        }
        if (_currentPage == "Weekly Details") RefreshWeeklyExperience();
    }

    private void RefreshDashboardStatus()
    {
        if (_pages.Count == 0 || IsDisposed) return;
        var now = DateTimeOffset.Now;
        var running = _session.IsTf2Running;
        _dashboardTime.Text = !string.IsNullOrWhiteSpace(_currentSessionId) && _managedSessionStartedAt is DateTimeOffset started
            ? FormatElapsed(now - started) : "00:00:00";
        _dashboardMonitor.Text = _dropMonitor.IsDegraded ? "● API degraded" : _dropMonitor.IsRunning ? "● Monitoring" : "○ Monitor off";
        _dashboardMonitor.Tag = _dropMonitor.IsDegraded ? "warning" : "good";
        _dashboardMonitor.ForeColor = _dropMonitor.IsDegraded ? Orange : Green;
        _dashboardAccount.Text = $"Pending: {_dropMonitor.PendingCount}  |  Last API check: {_dropMonitor.LastSuccessfulCheck?.ToLocalTime().ToString("HH:mm:ss") ?? "—"}";
        if (now >= _apiStatusRefreshAfter)
        {
            _apiPrimaryState.Text = "Primary key: " + (string.IsNullOrWhiteSpace(ApiKeyStore.Load()) ? "not configured" : "configured");
            _apiSecondaryState.Text = "Secondary key: " + (string.IsNullOrWhiteSpace(ApiKeyStore.LoadSecondary()) ? "not configured" : "configured (fallback)");
            _apiStatusRefreshAfter = now.AddMinutes(1);
        }
        if (!AccountDataContext.HasActiveAccount)
        {
            _dashboardDrops.Text = "—";
            _dashboardContainers.Text = "—";
            _dashboardEligible.Text = "00:00";
            _dashboardWeeklyEligible.Text = "00:00";
            _dashboardWeeklySummary.Text = "No Steam account selected";
            _dashboardWeeklyDetail.Text = "Detect an account to load weekly statistics.";
            _dashboardRemaining.Text = "—";
            _dashboardExpected.Text = "—";
            _dashboardApi.Text = "00:00";
            if (_recentSteamId is not null)
            {
                _recentSteamId = null;
                _recentFingerprint = 0;
                var obsolete = _recentDrops.Controls.Cast<Control>().ToArray();
                _recentDrops.Controls.Clear();
                foreach (var row in obsolete) row.Dispose();
            }
            return;
        }
        var regular = DropHistoryStore.GetCurrentWeekRegularEntries(now);
        var containers = DropHistoryStore.GetCurrentWeekSeparateTimerEntries(now);
        var usage = WeeklyUsageStore.GetCurrentWeek(now, regular.Count);
        var stats = DropHistoryStore.GetCurrentWeekIntervalStats(now, usage.EligibleTime);
        _dashboardWeeklyEligible.Text = FormatDuration(usage.EligibleTime);
        _weeklyProgressRatio = Math.Clamp(usage.EligibleTime.TotalMinutes / 600.0, 0, 1);
        _weeklyProgress.Invalidate();
        var sessionEligible = string.IsNullOrWhiteSpace(_currentSessionId)
            ? TimeSpan.Zero : WeeklyUsageStore.GetSessionEligibleTime();
        _dashboardEligible.Text = FormatDuration(sessionEligible);
        _dashboardWeeklySummary.Text = $"Regular drops: {regular.Count}   ·   Accepted: {regular.Count(x => x.AcknowledgedAt is not null)}";
        var lastRegular = regular.FirstOrDefault();
        _dashboardWeeklyDetail.Text = usage.BasePoolLikelyExhausted
            ? "~10h reference reached (NOT a verified weekly drop cap)"
            : lastRegular is null
                ? $"No regular drops this week   ·   reset {usage.WindowEndUtc:ddd HH:mm} UTC"
                : $"Last: {lastRegular.DisplayName}   ·   reset {usage.WindowEndUtc:ddd HH:mm} UTC";
        var sessionDrops = DropHistoryStore.GetSessionEntries(_currentSessionId)
            .Where(x => x.CountsTowardWeeklyMetrics).ToList();
        _dashboardDrops.Text = $"{sessionDrops.Count} / {sessionDrops.Count(x => x.AcknowledgedAt is not null)}";
        _dashboardContainers.Text = containers.Count.ToString();
        _dashboardRemaining.Text = usage.BasePoolLikelyExhausted || usage.EstimatedDropsRemainingHigh == 0
            ? "Unknown" : $"~{usage.EstimatedDropsRemainingLow}-{usage.EstimatedDropsRemainingHigh}";
        _dashboardExpected.Text = WeeklyStatisticsPresentation.NextCompact(
            !string.IsNullOrWhiteSpace(_currentSessionId) && _session.IsTf2Running,
            _dropMonitor.IsDegraded, _dropMonitor.PendingCount > 0, usage, stats);
        _dashboardApi.Text = FormatDuration(string.IsNullOrWhiteSpace(_currentSessionId) ? TimeSpan.Zero : GetCurrentSessionApiOutageDuration(now));
        var fingerprint = HashCode.Combine(regular.Count, containers.Count, regular.Count(x => x.AcknowledgedAt is not null), containers.Count(x => x.AcknowledgedAt is not null), AccountDataContext.ActiveSteamId64);
        if (fingerprint != _recentFingerprint || _recentSteamId != AccountDataContext.ActiveSteamId64 || now - _recentRefreshed > TimeSpan.FromMinutes(2))
        {
            _recentFingerprint = fingerprint;
            _recentSteamId = AccountDataContext.ActiveSteamId64;
            _recentRefreshed = now;
            RefreshRecentDrops();
        }
        _accountBanner.Text = AccountDataContext.DescribeActiveAccount();
        if (_currentPage == "Weekly Details" && now >= _nextWeeklyRefresh)
            RefreshWeeklyExperience();
    }

    private void RefreshRecentDrops()
    {
        if (_recentDrops.IsDisposed) return;
        _historyArtworkResolver.ReloadFromDiskIfChanged();
        _recentDrops.SuspendLayout();
        var previousRows = _recentDrops.Controls.Cast<Control>().ToArray();
        _recentDrops.Controls.Clear();
        foreach (var old in previousRows) old.Dispose();
        var entries = AccountDataContext.HasActiveAccount ? DropHistoryStore.GetCurrentWeekEntries(DateTimeOffset.Now) : Array.Empty<DropHistoryEntry>();
        if (entries.Count == 0)
        {
            var empty = UiLabel("No drops recorded this week.\n\nNew drops will appear here.", 10, false, "muted");
            empty.Size = new Size(280, 100);
            _recentDrops.Controls.Add(empty);
        }
        else
        {
            foreach (var entry in entries.Take(12))
            {
                var row = new Panel { Width = Math.Max(250, _recentDrops.ClientSize.Width - 28), Height = 94, Tag = "field", Margin = new Padding(0, 0, 0, 8) };
                var icon = new PictureBox { Location = new Point(8, 11), Size = new Size(70, 70), SizeMode = PictureBoxSizeMode.Zoom, Tag = "editor" };
                var fallback = new Label { Text = entry.CountsTowardWeeklyMetrics ? "✦" : "▣", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Symbol", 26), Tag = "warning" };
                icon.Controls.Add(fallback);
                row.Controls.Add(icon);
                var title = UiLabel(entry.DisplayName, 9.5f, true);
                title.Bounds = new Rectangle(86, 9, Math.Max(135, row.Width - 92), 27);
                title.Dock = DockStyle.None;
                row.Controls.Add(title);
                var type = UiLabel(entry.CountsTowardWeeklyMetrics ? "REGULAR" : "CONTAINER", 8.0f, false, "muted");
                type.Bounds = new Rectangle(86, 39, 135, 20);
                type.Dock = DockStyle.None;
                row.Controls.Add(type);
                var status = UiLabel((entry.AcknowledgedAt is null ? "● Pending" : "✓ Accepted") + "   " + entry.DetectedAt.ToLocalTime().ToString("dd MMM HH:mm"), 8.4f, false, entry.AcknowledgedAt is null ? "warning" : "good");
                status.Bounds = new Rectangle(86, 64, Math.Max(135, row.Width - 92), 23);
                status.Dock = DockStyle.None;
                row.Controls.Add(status);
                _recentDrops.Controls.Add(row);
                var url = _historyArtworkResolver.GetCachedImageUrl(entry.DefIndex);
                if (!string.IsNullOrWhiteSpace(url)) _ = DashboardItemImages.FillAsync(icon, fallback, url);
            }
        }
        _recentDrops.ResumeLayout();
        ApplyDashboardTheme();
        ScheduleHistoricalArtwork();
    }

    private void ScheduleHistoricalArtwork()
    {
        if (_historyArtworkLoading || IsDisposed || _dropMonitor.IsDegraded ||
            DateTimeOffset.UtcNow < _nextArtworkWarmAt || !AccountDataContext.HasActiveAccount)
            return;
        var key = ApiKeyStore.Load();
        if (string.IsNullOrWhiteSpace(key)) return;
        var missing = DropHistoryStore.GetCurrentWeekEntries(DateTimeOffset.Now)
            .Take(12).Select(x => x.DefIndex).Distinct()
            .Where(x => _historyArtworkResolver.GetCachedImageUrl(x) is null).ToArray();
        if (missing.Length == 0) return;
        _historyArtworkLoading = true;
        _nextArtworkWarmAt = DateTimeOffset.UtcNow.AddMinutes(12);
        _ = WarmDashboardArtworkAsync(key, missing);
    }

    private async Task WarmDashboardArtworkAsync(string key, uint[] indices)
    {
        try
        {
            var loaded = await _historyArtworkResolver.WarmHistoricalImagesAsync(
                key, indices, _historyArtworkCancellation.Token);
            if (loaded > 0 && !IsDisposed)
            {
                AppLogger.Info($"Dashboard historical artwork loaded: {loaded} item definition(s).");
                _recentRefreshed = DateTimeOffset.MinValue;
                RefreshRecentDrops();
                if (_currentPage == "History") RefreshAuxiliaryPages();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLogger.Warning($"Dashboard artwork refresh skipped: {ex.GetType().Name}"); }
        finally { _historyArtworkLoading = false; }
    }

    private void ApplyDashboardTheme()
    {
        if (_dashboardThemeApplying) return;
        _dashboardThemeApplying = true;
        try
        {
            var dark = _settings.DarkMode;
            var foreground = dark ? Color.FromArgb(235, 241, 246) : Color.FromArgb(27, 39, 53);
            var baseColor = dark ? DarkCanvas : Color.FromArgb(239, 243, 248);
            void Style(Control item)
            {
                var tag = item.Tag as string ?? "";
                item.BackColor = tag switch
                {
                    "sidebar" => dark ? DarkSide : Color.FromArgb(228, 234, 243),
                    "card" => dark ? DarkCard : Color.White,
                    "field" or "editor" => dark ? DarkField : Color.FromArgb(247, 249, 251),
                    "nav" => dark ? DarkSide : Color.FromArgb(228, 234, 243),
                    "start" => Color.FromArgb(49, 147, 75),
                    "stop" => dark ? Color.FromArgb(65, 76, 91) : Color.FromArgb(220, 229, 238),
                    "button" => dark ? DarkField : Color.FromArgb(218, 230, 243),
                    _ => item is TextBoxBase or ComboBox or DataGridView ? (dark ? DarkField : Color.White)
                        : item.Parent?.BackColor ?? baseColor
                };
                item.ForeColor = tag switch
                {
                    "muted" => dark ? DarkMuted : Color.FromArgb(91, 107, 126),
                    "good" => dark ? Green : Color.FromArgb(27, 130, 66),
                    "warning" => dark ? Orange : Color.FromArgb(174, 98, 25),
                    "highlight" => dark ? Color.White : Color.FromArgb(23, 55, 94),
                    "start" => Color.White,
                    _ => foreground
                };
                if (item is Button btn)
                {
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderSize = 0;
                    if (tag == "nav" && btn == _navButtons.GetValueOrDefault(_currentPage))
                    {
                        btn.BackColor = dark ? Color.FromArgb(31, 76, 128) : Color.FromArgb(188, 213, 247);
                        btn.ForeColor = dark ? Color.White : Color.FromArgb(21, 70, 130);
                    }
                }
                if (item is ThemedCheckBox checkbox) checkbox.DarkAppearance = dark;
                if (item is ThemedButton themedButton) themedButton.DarkAppearance = dark;
                if (item is DataGridView dgv) DashboardStyleGrid(dgv, dark);
                foreach (Control child in item.Controls) Style(child);
            }
            Style(this);
            _recentDrops.BackColor = dark ? DarkCard : Color.White;
            _historyGrid.BackgroundColor = dark ? DarkCard : Color.White;
            DashboardStyleGrid(_weeklyTrendGrid, dark);
        }
        finally { _dashboardThemeApplying = false; }
    }

    private static void DashboardStyleGrid(DataGridView grid, bool dark)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = dark ? DarkCard : Color.White;
        grid.GridColor = dark ? DarkLine : Color.LightGray;
        grid.DefaultCellStyle.BackColor = dark ? DarkCard : Color.White;
        grid.DefaultCellStyle.ForeColor = dark ? Color.WhiteSmoke : Color.FromArgb(35, 45, 60);
        grid.DefaultCellStyle.SelectionBackColor = Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = dark ? DarkField : Color.FromArgb(225, 235, 245);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = dark ? Color.White : Color.Black;
    }
}
