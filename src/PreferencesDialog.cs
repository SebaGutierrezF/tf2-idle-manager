namespace TF2IdleManager;

// Organized settings UI. Saves the same AppSettings / encrypted API key as earlier versions.
public sealed class PreferencesDialog : Form
{
    private readonly ComboBox _notificationCorner = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 225 };
    private ManagerToastForm? _previewToast;
    private readonly CheckBox _dark = new ThemedCheckBox { Text = "Dark mode", AutoSize = true };
    private readonly CheckBox _apiAlerts = new ThemedCheckBox { Text = "Show Steam API degraded/recovered notifications", AutoSize = true };
    private readonly CheckBox _repeat = new ThemedCheckBox { Text = "Repeat pending-drop alerts", AutoSize = true };
    private readonly CheckBox _forever = new ThemedCheckBox { Text = "Repeat until acknowledged (no maximum)", AutoSize = true };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 100, Width = 85 };
    private readonly NumericUpDown _seconds = new() { Minimum = 5, Maximum = 3600, Width = 85 };
    private readonly TextBox _secondaryKey = new() { UseSystemPasswordChar = true, Width = 350 };
    private readonly ListBox _servers = new() { Height = 210, Width = 440 };
    private readonly TextBox _newServer = new() { PlaceholderText = "server.example.com:27015", Width = 290 };
    private readonly CheckBox _tryBackupServers = new ThemedCheckBox
    { Text = "Try saved backup servers if the active server is full/unreachable", AutoSize = true };
    private readonly NumericUpDown _joinWait = new() { Minimum = 180, Maximum = 900, Increment = 30, Width = 90 };

    private static FlowLayoutPanel TabStack(TabPage tab)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(14)
        };
        tab.Controls.Add(panel);
        return panel;
    }

    private static Label TextLine(string value, int height = 40) => new()
    {
        Text = value, Height = height, Width = 465,
        AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft
    };

    private static void Add(FlowLayoutPanel parent, Control control)
    {
        control.Margin = new Padding(4, 6, 4, 7);
        parent.Controls.Add(control);
    }

    public PreferencesDialog(AppSettings settings)
    {
        Text = "TF2 Idle Manager — Settings";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(610, 545);
        MinimumSize = new Size(570, 455);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(shell);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        shell.Controls.Add(tabs, 0, 0);
        var appearance = new TabPage("Appearance");
        var notifications = new TabPage("Notifications");
        var api = new TabPage("API keys");
        var servers = new TabPage("Servers");
        tabs.TabPages.AddRange(new[] { appearance, notifications, api, servers });

        var look = TabStack(appearance);
        Add(look, TextLine("Color theme", 30));
        _dark.Checked = settings.DarkMode;
        Add(look, _dark);
        Add(look, TextLine("Changes to the main dashboard take effect after saving.", 42));

        var alerts = TabStack(notifications);
        Add(alerts, TextLine("Screen corner (all app-owned alerts)", 30));
        var cornerRow = new FlowLayoutPanel { Width = 490, Height = 43, WrapContents = false };
        foreach (var option in ToastPlacement.Choices) _notificationCorner.Items.Add(option.Label);
        var selectedCorner = ToastPlacement.Normalize(settings.NotificationCorner);
        _notificationCorner.SelectedIndex = Math.Max(0, Array.FindIndex(ToastPlacement.Choices, x => x.Value == selectedCorner));
        var preview = new ThemedButton { Text = "Preview", Width = 115, Height = 30 };
        preview.Click += (_, _) =>
        {
            var selected = ToastPlacement.Choices[Math.Max(0, _notificationCorner.SelectedIndex)].Value;
            if (_previewToast is { IsDisposed: false }) _previewToast.Close();
            var toast = new ManagerToastForm("TF2 notification preview", "This screen corner will be used for your next alert.", _dark.Checked);
            _previewToast = toast;
            var screen = Screen.FromControl(this);
            ToastPlacement.Apply(toast, screen.WorkingArea, selected);
            toast.Show();
            var final = ToastPlacement.Apply(toast, screen.WorkingArea, selected);
            AppLogger.Info($"NOTIFICATION PREVIEW POSITION | corner={selected} | monitor={screen.DeviceName} | target={final.X},{final.Y} | actual={toast.Left},{toast.Top}");
        };
        cornerRow.Controls.Add(_notificationCorner);
        cornerRow.Controls.Add(preview);
        Add(alerts, cornerRow);
        _repeat.Checked = settings.RepeatDropAlerts;
        _forever.Checked = settings.RepeatUntilAcknowledged;
        _apiAlerts.Checked = settings.ShowApiStatusNotifications;
        _count.Value = Math.Clamp(settings.RepeatCount, 1, 100);
        _seconds.Value = Math.Clamp(settings.RepeatIntervalSeconds, 5, 3600);
        Add(alerts, _repeat);
        Add(alerts, _forever);
        var repeatRow = new FlowLayoutPanel { Width = 490, Height = 47, WrapContents = false };
        repeatRow.Controls.AddRange(new Control[] {
            new Label { Text = "Extra repeats", Width = 106, Height = 30, TextAlign = ContentAlignment.MiddleLeft },
            _count,
            new Label { Text = "Every (seconds)", Width = 140, Height = 30, TextAlign = ContentAlignment.MiddleLeft },
            _seconds
        });
        Add(alerts, repeatRow);
        Add(alerts, _apiAlerts);
        Add(alerts, TextLine("Repeated alerts stop after the item is acknowledged. An API outage can delay that confirmation.", 55));

        var apiPage = TabStack(api);
        Add(apiPage, TextLine("Backup Steam Web API key (optional)", 34));
        _secondaryKey.Text = ApiKeyStore.LoadSecondary() ?? "";
        Add(apiPage, _secondaryKey);
        Add(apiPage, TextLine("Saved encrypted using your Windows user account (DPAPI).", 45));
        Add(apiPage, TextLine("A second key may help with credential-specific problems; it cannot resolve a Valve-wide HTTP 503 outage.", 75));
        Add(apiPage, TextLine("Primary API key is configured on the Dashboard.", 42));

        var serverPage = TabStack(servers);
        Add(serverPage, TextLine("Full-server protection / join watchdog", 34));
        _tryBackupServers.Checked = settings.TryBackupServersWhenFull;
        _joinWait.Value = Math.Clamp(settings.ServerJoinTimeoutSeconds, 180, 900);
        Add(serverPage, _tryBackupServers);
        var timeoutRow = new FlowLayoutPanel { Width = 490, Height = 42, WrapContents = false };
        timeoutRow.Controls.Add(new Label { Text = "Join timeout (seconds)", Width = 185, Height = 30, TextAlign = ContentAlignment.MiddleLeft });
        timeoutRow.Controls.Add(_joinWait);
        Add(serverPage, timeoutRow);
        Add(serverPage, TextLine("Only servers saved below are considered for automatic failover. Without backups, the same server is retried at a limited cadence.", 60));
        Add(serverPage, TextLine("Saved idle servers", 32));
        foreach (var server in settings.ServerProfiles ?? new List<string>()) _servers.Items.Add(server);
        Add(serverPage, _servers);
        var srvRow = new FlowLayoutPanel { Width = 490, Height = 45, WrapContents = false };
        var addServer = new ThemedButton { Text = "Add", Width = 67, Height = 32 };
        var removeServer = new ThemedButton { Text = "Remove", Width = 83, Height = 32 };
        addServer.Click += (_, _) =>
        {
            var value = _newServer.Text.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !_servers.Items.Contains(value))
            { _servers.Items.Add(value); _newServer.Clear(); }
        };
        removeServer.Click += (_, _) => { if (_servers.SelectedIndex >= 0) _servers.Items.RemoveAt(_servers.SelectedIndex); };
        srvRow.Controls.AddRange(new Control[] { _newServer, addServer, removeServer });
        Add(serverPage, srvRow);

        var saveRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new ThemedButton { Text = "Save settings", Width = 160, Height = 35 };
        var cancel = new ThemedButton { Text = "Cancel", Width = 100, Height = 35 };
        saveRow.Controls.Add(save);
        saveRow.Controls.Add(cancel);
        shell.Controls.Add(saveRow, 0, 1);
        AcceptButton = save;
        CancelButton = cancel;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        save.Click += (_, _) =>
        {
            try
            {
                ApiKeyStore.SaveSecondary(_secondaryKey.Text);
                settings.DarkMode = _dark.Checked;
                settings.RepeatDropAlerts = _repeat.Checked;
                settings.RepeatUntilAcknowledged = _forever.Checked;
                settings.RepeatCount = (int)_count.Value;
                settings.RepeatIntervalSeconds = (int)_seconds.Value;
                settings.ShowApiStatusNotifications = _apiAlerts.Checked;
                settings.NotificationCorner = ToastPlacement.Choices[Math.Max(0, _notificationCorner.SelectedIndex)].Value;
                settings.ServerProfiles = _servers.Items.Cast<string>().ToList();
                settings.TryBackupServersWhenFull = _tryBackupServers.Checked;
                settings.ServerJoinTimeoutSeconds = (int)_joinWait.Value;
                SettingsStore.Save(settings);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot save settings"); }
        };
        FormClosed += (_, _) => { if (_previewToast is { IsDisposed: false }) _previewToast.Close(); };
    }
}
