using System.Diagnostics;
using System.Media;

namespace TF2IdleManager;

public sealed partial class MainForm : Form
{
    private readonly Label _steamStatus = new();
    private readonly Label _tf2Status = new();
    private readonly Label _sessionStatus = new();
    private readonly Label _elapsedLabel = new();
    private readonly Label _serverStatus = new();
    private readonly Label _autoHeavyStatus = new();
    private readonly Label _dropStatus = new();
    private readonly Label _weeklyStatus = new();
    private readonly Label _watchdogStatus = new();
    private readonly Label _weeklyDetailStatus = new();
    private readonly Label _sessionStatsStatus = new();

    private readonly TextBox _serverBox = new();
    private readonly TextBox _apiKeyBox = new();
    private readonly CheckBox _attackCheck = new ThemedCheckBox();
    private readonly CheckBox _leftCheck = new ThemedCheckBox();
    private readonly ComboBox _classCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _minimizeCheck = new ThemedCheckBox();
    private readonly CheckBox _closeOnStopCheck = new ThemedCheckBox();
    private readonly CheckBox _dropNotificationsCheck = new ThemedCheckBox();
    private readonly CheckBox _autoReconnectCheck = new ThemedCheckBox();
    private readonly TextBox _extraOptionsBox = new();

    private readonly Button _detectButton = new ThemedButton();
    private readonly Button _startButton = new ThemedButton();
    private readonly Button _stopButton = new ThemedButton();
    private readonly Button _openTf2Button = new ThemedButton();
    private readonly Button _openLogsButton = new ThemedButton();
    private readonly Button _preferencesButton = new ThemedButton();
    private readonly Button _historyButton = new ThemedButton();
    private readonly ComboBox _savedServerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _weeklyDetailsButton = new ThemedButton();
    private readonly Button _testNotificationButton = new ThemedButton();
    private readonly Button _saveApiKeyButton = new ThemedButton();
    private readonly Button _checkApiNowButton = new ThemedButton();

    private readonly RichTextBox _logBox = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly Dictionary<ulong, (PendingDropItem Item, int Sent, DateTimeOffset Next)> _reminders = new();
    private DropToastForm? _activeToast;
    private readonly List<Form> _toastWindows = new();
    private readonly DropMonitor _dropMonitor = new();

    private AppSettings _settings;
    private SteamDetection _detection;
    private Tf2SessionManager _session;
    private bool _busy;
    private string? _lastDropItem;
    private string? _currentSessionId;
    private DateTimeOffset _lastSessionLedgerFlush = DateTimeOffset.MinValue;
    private DateTimeOffset? _managedSessionStartedAt;
    private bool _autoHeavyReadyForDrops;
    private bool _watchdogBusy;
    private bool _watchdogCircuitOpen;
    private DateTimeOffset _lastWatchdogPoll = DateTimeOffset.MinValue;
    private readonly Queue<DateTimeOffset> _watchdogRecoveries = new();
    private readonly HashSet<string> _joinTriedServers = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _nextJoinRetryAt;
    private int _sameServerJoinRetries;
    private bool _joinWarningActive;
    private bool _postTransitionRecheckPending;
    private bool _dropMonitorWasDegraded;
    private TimeSpan _sessionApiOutageAccumulated = TimeSpan.Zero;
    private DateTimeOffset? _sessionApiOutageStartedAt;

    public MainForm()
    {
        Text = "TF2 Idle Manager v1.0.0";
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "TF2IdleManager.ico")); }
        catch { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 740);
        Size = new Size(1440, 920);

        _settings = SettingsStore.Load();
        _detection = SteamLocator.Detect();
        var legacyMigration = LegacyAccountMigration.TryMigrateGlobalData();
        ActivateAccountContext(_detection);
        _session = new Tf2SessionManager(_detection);

        BuildUi();
        ConfigureNotifications();
        LoadSettingsIntoUi();
        WireEvents();
        ThemeHelper.Apply(this, _settings.DarkMode);
        ApplyDashboardTheme();
        RefreshDetectionUi();
        RefreshSessionUi();

        AppLogger.LineWritten += OnLogLine;
        _session.AutoHeavyStatusChanged += OnAutoHeavyStatusChanged;
        _dropMonitor.DropDetected += OnDropDetected;
        _dropMonitor.DropAcknowledged += OnDropAcknowledged;
        _dropMonitor.SnapshotChanged += OnDropSnapshotChanged;
        _dropMonitor.StatusChanged += OnDropMonitorStatusChanged;
        _dropMonitor.AvailabilityChanged += OnDropMonitorAvailabilityChanged;
        _dropMonitor.BaselineEstablished += OnDropBaselineEstablished;

        AppendLog("Ready. v1.0.0: verified Auto Class, server-join watchdog, account-separated drop monitoring and session history.");
        AppendLog("Notifications support images, repetition and configurable screen corners. Steam Web API keys are protected with Windows DPAPI.");
        AppendLog("Auto Class keyboard flow is unchanged. The watchdog now detects server/map lifecycle resets even when tf_win64.exe stays alive and re-runs Auto Class without rebuilding the drop baseline.");
        AppendLog("Shared app settings/API key/item-name schema cache stay global; account-specific drop/weekly data lives under accounts/<SteamID64>/.");
        if (!string.IsNullOrWhiteSpace(_detection.PersonaName))
            AppendLog($"Steam account detected: {_detection.PersonaName} ({_detection.SteamId64 ?? "SteamID unavailable"})");

        if (legacyMigration.LegacyDataFound)
        {
            AppendLog(legacyMigration.Migrated
                ? $"↺ ACCOUNT DATA MIGRATION: {legacyMigration.Message}"
                : legacyMigration.Ambiguous
                    ? $"⚠ ACCOUNT DATA MIGRATION: {legacyMigration.Message}"
                    : $"Account data migration: {legacyMigration.Message}");
        }

        ReconcileCurrentAccountHistory();

        _uiTimer.Interval = 1000;
        _uiTimer.Tick += async (_, _) =>
        {
            UpdateWeeklyUsageTracking();
            FlushManagedSessionLedger();
            CheckRepeatedAlerts();
            RefreshSessionUi();
            await CheckWatchdogAsync();
        };
        _uiTimer.Start();

        Shown += async (_, _) =>
        {
            await ReconcileActiveAccountWithApiAsync("startup");
        };

        FormClosed += (_, _) =>
        {
            AppLogger.LineWritten -= OnLogLine;
            _session.AutoHeavyStatusChanged -= OnAutoHeavyStatusChanged;
            _dropMonitor.DropDetected -= OnDropDetected;
            _dropMonitor.DropAcknowledged -= OnDropAcknowledged;
            _dropMonitor.SnapshotChanged -= OnDropSnapshotChanged;
            _dropMonitor.StatusChanged -= OnDropMonitorStatusChanged;
            _dropMonitor.AvailabilityChanged -= OnDropMonitorAvailabilityChanged;
            _dropMonitor.BaselineEstablished -= OnDropBaselineEstablished;
            var closedAt = DateTimeOffset.Now;
            WeeklyUsageStore.StopSession(closedAt, IsCurrentlyDropEligible());
            FinalizeManagedSessionLedger(closedAt);
            _dropMonitor.Dispose();
            _reminders.Clear();
            foreach (var toast in _toastWindows.ToArray())
                if (!toast.IsDisposed) toast.Close();
            _session.CleanupOnAppExit();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        };
    }

    private void ConfigureNotifications()
    {
        _notifyIcon.Text = "TF2 Idle Manager";
        _notifyIcon.Icon = Icon ?? SystemIcons.Application;
        _notifyIcon.Visible = true;
        _notifyIcon.BalloonTipClicked += (_, _) => _session.BringTf2ToFront();
    }

    private void WireEvents()
    {
        _detectButton.Click += async (_, _) => await DetectAgainAsync();
        _startButton.Click += async (_, _) => await StartIdleAsync();
        _stopButton.Click += async (_, _) => await StopIdleAsync();
        _openTf2Button.Click += (_, _) =>
        {
            if (!_session.BringTf2ToFront())
                MessageBox.Show("TF2 is not running or its window is not ready.", "TF2 Idle Manager");
        };
        _openLogsButton.Click += (_, _) => OpenFolder(AppLogger.LogDirectory);
        _weeklyDetailsButton.Click += (_, _) => ShowWeeklyDetails();
        _preferencesButton.Click += (_, _) => OpenPreferences();
        _historyButton.Click += (_, _) => OpenHistory();
        _savedServerCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_savedServerCombo.SelectedIndex > 0 && _savedServerCombo.SelectedItem is string v && !string.IsNullOrWhiteSpace(v) && !_session.IsTf2Running)
                _serverBox.Text = v;
        };
        _testNotificationButton.Click += (_, _) => ShowTestNotification();
        _saveApiKeyButton.Click += (_, _) => SaveApiKey();
        _checkApiNowButton.Click += async (_, _) => await CheckApiNowAsync();

        _serverBox.TextChanged += (_, _) => SaveSettingsFromUi();
        _attackCheck.CheckedChanged += (_, _) => SaveSettingsFromUi();
        _leftCheck.CheckedChanged += (_, _) => SaveSettingsFromUi();
        _classCombo.SelectedIndexChanged += (_, _) => SaveSettingsFromUi();
        _minimizeCheck.CheckedChanged += (_, _) => SaveSettingsFromUi();
        _closeOnStopCheck.CheckedChanged += (_, _) => SaveSettingsFromUi();
        _dropNotificationsCheck.CheckedChanged += (_, _) => SaveSettingsFromUi();
        _autoReconnectCheck.CheckedChanged += (_, _) => SaveSettingsFromUi();
        _extraOptionsBox.TextChanged += (_, _) => SaveSettingsFromUi();
    }

    private void LoadSettingsIntoUi()
    {
        _serverBox.Text = _settings.ServerAddress;
        _attackCheck.Checked = _settings.AttackEnabled;
        _leftCheck.Checked = _settings.LeftEnabled;
        _classCombo.SelectedItem = Tf2Classes.Normalize(_settings.EffectiveClass);
        if (_classCombo.SelectedIndex < 0)
            _classCombo.SelectedItem = "Random";
        RefreshServerProfiles();
        _minimizeCheck.Checked = _settings.MinimizeTf2AfterLaunch;
        _closeOnStopCheck.Checked = _settings.CloseTf2OnStop;
        _dropNotificationsCheck.Checked = _settings.DropNotificationsEnabled;
        _autoReconnectCheck.Checked = _settings.AutoReconnectEnabled;
        _extraOptionsBox.Text = _settings.ExtraLaunchOptions;
        _apiKeyBox.Text = ApiKeyStore.Load() ?? string.Empty;
    }

    private void SaveSettingsFromUi()
    {
        _settings.ServerAddress = _serverBox.Text.Trim();
        _settings.AttackEnabled = _attackCheck.Checked;
        _settings.LeftEnabled = _leftCheck.Checked;
        _settings.AutoSelectHeavy = true; // Legacy setting retained for compatibility.
        _settings.AutoClass = _classCombo.SelectedItem?.ToString() ?? "Random";
        _settings.MinimizeTf2AfterLaunch = _minimizeCheck.Checked;
        _settings.CloseTf2OnStop = _closeOnStopCheck.Checked;
        _settings.DropNotificationsEnabled = _dropNotificationsCheck.Checked;
        _settings.AutoReconnectEnabled = _autoReconnectCheck.Checked;
        _settings.ExtraLaunchOptions = _extraOptionsBox.Text.Trim();
        SettingsStore.Save(_settings);
    }

    private void SaveApiKey()
    {
        try
        {
            ApiKeyStore.Save(_apiKeyBox.Text);
            AppendLog(string.IsNullOrWhiteSpace(_apiKeyBox.Text)
                ? "Saved Steam Web API key was cleared."
                : "Steam Web API key saved encrypted for this Windows user (DPAPI)."
            );
            _apiStatusRefreshAfter = DateTimeOffset.MinValue;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TF2 Idle Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CheckApiNowAsync()
    {
        if (_busy)
            return;

        EnsureFreshSteamAccountContext();
        _busy = true;
        RefreshSessionUi();
        try
        {
            var steamId = RequireSteamId();
            AppendLog("Checking TF2 inventory for unacknowledged drop positions...");
            var snapshot = await _dropMonitor.CheckNowAsync(_apiKeyBox.Text, steamId);
            var reconciledAccepted = DropHistoryStore.ReconcileAcknowledgementState(snapshot, snapshot.CheckedAt);
            foreach (var item in snapshot.PendingItems)
                DropHistoryStore.RecordRecoveredPending(item, snapshot.CheckedAt, recoveryReason: "manual-check-baseline");
            if (reconciledAccepted.Count > 0)
                AppendLog($"↺ Manual check reconciled {reconciledAccepted.Count} previously tracked item(s) as accepted while this account was not monitored.");
            RefreshWeeklyUi();

            if (snapshot.PendingCount == 0)
            {
                AppendLog("Steam API check OK: no pending TF2 drop detected.");
            }
            else
            {
                AppendLog($"Steam API check OK: {snapshot.PendingCount} pending TF2 item(s) detected.");
                foreach (var item in snapshot.PendingItems)
                    AppendLog($"  • {item.DisplayName} | item={item.ItemId} | defindex={item.DefIndex} | inventory={item.InventoryPosition}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Manual Steam API check failed: {ex.Message}");
            MessageBox.Show(
                ex.Message + "\n\nCheck the API key, SteamID detection, and TF2 inventory privacy.",
                "TF2 drop check",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
        finally
        {
            _busy = false;
            RefreshSessionUi();
        }
    }

    private async Task DetectAgainAsync()
    {
        var previousSteamId = AccountDataContext.ActiveSteamId64;
        _detection = SteamLocator.Detect();
        _session.UpdateDetection(_detection);
        var accountChanged = ActivateAccountContext(_detection);

        if (accountChanged)
        {
            _lastDropItem = null;
            _dropMonitor.Stop();
            _dropMonitorWasDegraded = false;
            AppendLog($"ACCOUNT SWITCH: {previousSteamId ?? "none"} -> {_detection.SteamId64 ?? "unavailable"}. Weekly/drop data context changed without mixing histories.");
            ReconcileCurrentAccountHistory();
            await ReconcileActiveAccountWithApiAsync("account switch");
        }

        RefreshDetectionUi();
        AppendLog(_detection.Ready
            ? $"Steam and TF2 detected successfully. Active data account: {AccountDataContext.DescribeActiveAccount()}."
            : "Detection incomplete. Check that Steam and TF2 are installed.");
    }

    private void RefreshDetectionUi()
    {
        var identity = string.IsNullOrWhiteSpace(_detection.PersonaName)
            ? ""
            : $" | Player: {_detection.PersonaName}";
        var steamId = string.IsNullOrWhiteSpace(_detection.SteamId64)
            ? ""
            : $" | SteamID64: {_detection.SteamId64}";

        _steamStatus.Text = _detection.SteamFound
            ? $"● Steam: {_detection.SteamDirectory}{identity}{steamId}"
            : "○ Steam: not detected";

        _tf2Status.Text = _detection.Tf2Found
            ? $"● TF2: {_detection.Tf2Directory}"
            : "○ TF2: not detected";

        RefreshSessionUi();
    }

    private void RefreshSessionUi()
    {
        var running = _session.IsTf2Running;
        var managedActive = !string.IsNullOrWhiteSpace(_currentSessionId);
        _sessionStatus.Text = running ? "● TF2 running" : managedActive ? "○ TF2 down (managed)" : "○ TF2 stopped";

        if (managedActive && _managedSessionStartedAt is DateTimeOffset started)
        {
            var elapsed = DateTimeOffset.Now - started;
            _elapsedLabel.Text = $"Session {FormatElapsed(elapsed)}";
        }
        else
        {
            _elapsedLabel.Text = "Session --:--:--";
        }

        _serverStatus.Text = string.IsNullOrWhiteSpace(_session.LastRequestedServer)
            ? string.IsNullOrWhiteSpace(_settings.ServerAddress) ? "Server —" : $"Server {_settings.ServerAddress}"
            : $"Server {_session.LastRequestedServer}";

        if (!running && !managedActive)
            _autoHeavyStatus.Text = $"Auto Class: {_settings.EffectiveClass} ready";

        if (!managedActive)
            _watchdogStatus.Text = _settings.AutoReconnectEnabled ? "Watchdog: ready" : "Watchdog: off";
        else if (_watchdogCircuitOpen)
            _watchdogStatus.Text = "Watchdog: paused — STOP/START";
        else if (_joinWarningActive && _session.IsServerJoinPending)
            _watchdogStatus.Text = _nextJoinRetryAt.HasValue ? "Watchdog: no slot — retry planned" : "Watchdog: no server slot — not idling";
        else if (_session.IsServerJoinPending)
            _watchdogStatus.Text = "Watchdog: waiting for server join...";
        else if (!_settings.AutoReconnectEnabled)
            _watchdogStatus.Text = running ? "Watchdog: observe only" : "Watchdog: TF2 down";
        else if (!_watchdogBusy)
            _watchdogStatus.Text = running ? "Watchdog: watching ✓" : "Watchdog: recovery pending";

        _startButton.Enabled = !_busy && _detection.Ready && !running && !managedActive;
        _stopButton.Enabled = !_busy && (running || managedActive);
        _openTf2Button.Enabled = running;
        _detectButton.Enabled = !_busy && !managedActive;
        _serverBox.Enabled = !_busy && !managedActive;
        _attackCheck.Enabled = !_busy && !managedActive;
        _leftCheck.Enabled = !_busy && !managedActive;
        _classCombo.Enabled = !_busy && !managedActive;
        _savedServerCombo.Enabled = !_busy && !managedActive;
        _minimizeCheck.Enabled = !_busy && !managedActive;
        _autoReconnectCheck.Enabled = !_busy && !managedActive;
        _closeOnStopCheck.Enabled = !_busy;
        _dropNotificationsCheck.Enabled = !_busy && !managedActive;
        _extraOptionsBox.Enabled = !_busy && !managedActive;
        _apiKeyBox.Enabled = !_busy;
        _saveApiKeyButton.Enabled = !_busy;
        _checkApiNowButton.Enabled = !_busy;
        _testNotificationButton.Enabled = !_busy;

        if (!running && !_dropMonitor.IsRunning && _lastDropItem is null)
            _dropStatus.Text = "Drop monitor: off";

        RefreshWeeklyUi();
    }

    private async Task StartIdleAsync()
    {
        if (_busy)
            return;

        EnsureFreshSteamAccountContext();
        SaveSettingsFromUi();
        _busy = true;
        _lastDropItem = null;
        _autoHeavyReadyForDrops = false;
        _watchdogBusy = false;
        _watchdogCircuitOpen = false;
        _watchdogRecoveries.Clear();
        _joinTriedServers.Clear();
        _joinTriedServers.Add(Tf2SessionManager.NormalizeServerForSettings(_settings.ServerAddress) ?? _settings.ServerAddress.Trim());
        _sameServerJoinRetries = 0;
        _nextJoinRetryAt = null;
        _joinWarningActive = false;
        _postTransitionRecheckPending = false;
        _lastWatchdogPoll = DateTimeOffset.MinValue;
        RefreshSessionUi();

        try
        {
            if (_settings.DropNotificationsEnabled)
            {
                _ = RequireSteamId();
                if (string.IsNullOrWhiteSpace(_apiKeyBox.Text))
                    throw new InvalidOperationException("Drop monitoring is enabled, but no Steam Web API key is entered.");
            }

            AppendLog($"Starting idle session [{AccountDataContext.DescribeActiveAccount()}] → {_settings.ServerAddress}");
            await _session.StartAsync(_settings);

            if (_session.IsTf2Running)
            {
                _currentSessionId = $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
                var sessionStartedAt = DateTimeOffset.Now;
                _managedSessionStartedAt = sessionStartedAt;
                WeeklyUsageStore.StartSession(sessionStartedAt);
                try { SessionHistoryStore.Start(_currentSessionId, sessionStartedAt, _session.LastRequestedServer, _settings.EffectiveClass); }
                catch (Exception ex) { AppLogger.Warning($"Session ledger unavailable; normal TF2 session continues: {ex.Message}"); }
                _lastSessionLedgerFlush = sessionStartedAt;
                _sessionApiOutageAccumulated = TimeSpan.Zero;
                _sessionApiOutageStartedAt = null;
                AppendLog($"TF2 started. Managed session {_currentSessionId} began; the idle CFG was loaded and the server connection was requested.");
                await StartDropMonitorIfEnabledAsync();
            }
            else
            {
                AppendLog("Steam accepted the launch request, but TF2 was not detected within 60 seconds.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("START IDLE failed.", ex);
            MessageBox.Show(ex.Message, "TF2 Idle Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            RefreshSessionUi();
        }
    }

    private async Task StartDropMonitorIfEnabledAsync()
    {
        _dropMonitor.Stop();
        _reminders.Clear();

        if (!_settings.DropNotificationsEnabled)
        {
            _dropStatus.Text = "Drop monitor: disabled";
            AppendLog("Drop notifications are disabled.");
            return;
        }

        try
        {
            var steamId = RequireSteamId();
            var baselineCount = await _dropMonitor.StartAsync(_apiKeyBox.Text, steamId);
            if (baselineCount >= 0)
            {
                AppendLog($"Drop monitor baseline created with {baselineCount} already-pending item(s). Known item IDs remain baseline; any pending item not present in drop-history.json is recovered and counted once with unknown exact drop time.");
                AppendLog("From now on, genuinely new item IDs notify normally. Accepting either a live or recovered pending item is detected automatically when it leaves the pending set.");
            }
            else
            {
                AppendLog("Steam API was temporarily unavailable while building the baseline. The monitor remains active; on the first successful snapshot, known IDs stay baseline and previously untracked pending IDs are recovered once without pretending their exact drop time is known.");
            }
        }
        catch (Exception ex)
        {
            _dropStatus.Text = "Drop monitor: configuration error";
            AppendLog($"Drop monitor could not start: {ex.Message}");
        }
    }

    private async Task StopIdleAsync()
    {
        if (_busy)
            return;

        _busy = true;
        var stoppedAt = DateTimeOffset.Now;
        WeeklyUsageStore.StopSession(stoppedAt, IsCurrentlyDropEligible());
        FinalizeManagedSessionLedger(stoppedAt);
        _dropMonitor.Stop();
        RefreshSessionUi();
        try
        {
            await _session.StopAsync(_settings);
            AppendLog(_settings.CloseTf2OnStop
                ? "Idle session stopped; TF2 closed."
                : "STOP requested. TF2 was left open because 'Close TF2 on STOP' is disabled.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("STOP failed.", ex);
            MessageBox.Show(ex.Message, "TF2 Idle Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _currentSessionId = null;
            _managedSessionStartedAt = null;
            _sessionApiOutageAccumulated = TimeSpan.Zero;
            _sessionApiOutageStartedAt = null;
            _watchdogBusy = false;
            _watchdogCircuitOpen = false;
            _watchdogRecoveries.Clear();
            _joinTriedServers.Clear();
            _nextJoinRetryAt = null;
            _joinWarningActive = false;
            _postTransitionRecheckPending = false;
            _sameServerJoinRetries = 0;
            _busy = false;
            RefreshSessionUi();
        }
    }

    private void OnDropDetected(DropDetectedEvent drop)
    {
        if (IsDisposed)
            return;

        void Apply()
        {
            DropHistoryStore.RecordDetected(drop, _currentSessionId, _session.LastRequestedServer, WeeklyUsageStore.GetEligibleTime(drop.DetectedAt).TotalSeconds);
            _lastDropItem = drop.DisplayName;

            if (drop.Item.IsSeparateTimerContainer)
            {
                _dropStatus.Text = $"📦 NEW CONTAINER: {drop.DisplayName}";
                AppendLog($"📦 TF2 SEPARATE-TIMER CONTAINER DETECTED: {drop.DisplayName} | item={drop.ItemId} | defindex={drop.DefIndex} | inventory={drop.InventoryPosition} | excluded from weekly regular-drop metrics");
            }
            else
            {
                _dropStatus.Text = $"🎁 NEW: {drop.DisplayName}";
                AppendLog($"🎁 NEW TF2 REGULAR DROP DETECTED: {drop.DisplayName} | item={drop.ItemId} | defindex={drop.DefIndex} | inventory={drop.InventoryPosition}");
            }

            AppendLog("Open TF2 and acknowledge the in-game item alert before the next item can occur.");
            ShowDropNotification(drop.Item);
            _reminders[drop.ItemId] = (drop.Item, 0,
                DateTimeOffset.Now.AddSeconds(Math.Clamp(_settings.RepeatIntervalSeconds, 5, 3600)));
            RefreshWeeklyUi();
        }

        if (InvokeRequired)
            BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private void OnDropAcknowledged(DropAcknowledgedEvent acknowledged)
    {
        if (IsDisposed)
            return;

        void Apply()
        {
            _reminders.Remove(acknowledged.Item.ItemId);
            var recoveredFallback = DropHistoryStore.RecordAcknowledged(acknowledged);
            if (recoveredFallback)
                AppendLog($"↺ RECOVERED UNTRACKED DROP while acknowledging: {acknowledged.Item.DisplayName} | item={acknowledged.Item.ItemId} | exact drop time unknown");
            AppendLog($"✓ DROP ACKNOWLEDGED: {acknowledged.Item.DisplayName} | item={acknowledged.Item.ItemId}");
            if (_lastDropItem == acknowledged.Item.DisplayName)
                _lastDropItem = null;
            RefreshWeeklyUi();
        }

        if (InvokeRequired)
            BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private void OnDropSnapshotChanged(DropSnapshot snapshot)
    {
        if (IsDisposed)
            return;

        void Apply()
        {
            var knownPending = snapshot.PendingItems.Select(x => x.ItemId).ToHashSet();
            foreach (var id in _reminders.Keys.ToArray())
                if (!knownPending.Contains(id)) _reminders.Remove(id);
            if (_lastDropItem is null)
                _dropStatus.Text = $"Pending: {snapshot.PendingCount} | monitor active";
        }

        if (InvokeRequired)
            BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private void RefreshWeeklyUi()
    {
        if (!AccountDataContext.HasActiveAccount)
        {
            RefreshDashboardStatus();
            _weeklyStatus.Text = "TF2 week: Steam account unavailable — no account data loaded";
            _weeklyDetailStatus.Text = "Detect a logged-in Steam account to load isolated drop/weekly statistics.";
            return;
        }

        var now = DateTimeOffset.Now;
        var weekly = DropHistoryStore.GetCurrentWeek(now);
        var usage = WeeklyUsageStore.GetCurrentWeek(now, weekly.DetectedCount);
        var intervalStats = DropHistoryStore.GetCurrentWeekIntervalStats(now, usage.EligibleTime);

        var estimate = WeeklyStatisticsPresentation.Remaining(usage);
        var nextExpected = WeeklyStatisticsPresentation.Next(
            !string.IsNullOrWhiteSpace(_currentSessionId) && _session.IsTf2Running,
            _dropMonitor.IsDegraded, _dropMonitor.PendingCount > 0, usage, intervalStats);

        var accountLabel = string.IsNullOrWhiteSpace(AccountDataContext.ActivePersonaName)
            ? AccountDataContext.ActiveSteamId64
            : AccountDataContext.ActivePersonaName;

        var regularEntries = DropHistoryStore.GetCurrentWeekRegularEntries(now);
        var separateTimerEntries = DropHistoryStore.GetCurrentWeekSeparateTimerEntries(now);

        _weeklyStatus.Text =
            $"TF2 week [{accountLabel}]: {weekly.DetectedCount} regular / {weekly.AcknowledgedCount} accepted | "
            + $"containers {separateTimerEntries.Count} | tracked potential idle {FormatDuration(usage.EligibleTime)} "
            + $"(reference ~10h) | remaining {estimate} | next: {nextExpected} | reset {usage.WindowEndUtc:ddd HH:mm} UTC";
        var lastDrop = regularEntries.FirstOrDefault();
        var lastText = lastDrop is null
            ? "Last regular drop: none recorded"
            : intervalStats.EligibleSinceLastDrop is null
                ? $"Last regular drop: {lastDrop.DisplayName} | eligible since: unknown"
                : $"Last regular drop: {lastDrop.DisplayName} | eligible since: {FormatMinutes(intervalStats.EligibleSinceLastDrop.Value)}";
        if (separateTimerEntries.Count > 0)
            lastText += $" | separate-timer containers: {separateTimerEntries.Count}";
        var personalText = intervalStats.SampleCount == 0
            ? "personal intervals: collecting samples"
            : $"personal avg {FormatMinutes(intervalStats.AverageInterval ?? TimeSpan.Zero)} | observed {FormatMinutes(intervalStats.ShortestInterval ?? TimeSpan.Zero)}-{FormatMinutes(intervalStats.LongestInterval ?? TimeSpan.Zero)}";
        var monitorText = _dropMonitor.IsDegraded
            ? "DROP ALERTS DEGRADED — Steam API unavailable; eligible timer paused"
            : "drop monitor healthy";
        _weeklyDetailStatus.Text = $"{lastText} | {personalText} | {monitorText} | measured locally; estimates are not exact Valve counters.";

        if (string.IsNullOrWhiteSpace(_currentSessionId))
        {
            _sessionStatsStatus.Text = "Session: idle — no managed session active.";
        }
        else
        {
            var sessionEntries = DropHistoryStore.GetSessionEntries(_currentSessionId);
            var sessionRegular = sessionEntries.Count(x => x.CountsTowardWeeklyMetrics);
            var sessionContainers = sessionEntries.Count - sessionRegular;
            var sessionAcceptedRegular = sessionEntries.Count(x => x.CountsTowardWeeklyMetrics && x.AcknowledgedAt is not null);
            var sessionEligible = WeeklyUsageStore.GetSessionEligibleTime();
            var sessionOutage = GetCurrentSessionApiOutageDuration(now);
            _sessionStatsStatus.Text =
                $"Session: eligible {FormatDuration(sessionEligible)} | regular {sessionRegular} ({sessionAcceptedRegular} accepted) | "
                + $"containers {sessionContainers} | API outage {FormatDuration(sessionOutage)}";
        }
        RefreshDashboardStatus();
    }

    private void UpdateWeeklyUsageTracking()
    {
        if (!_session.IsTf2Running)
            return;

        WeeklyUsageStore.Tick(DateTimeOffset.Now, IsCurrentlyDropEligible());
    }


    // Persist measured session metrics independently of the Thursday weekly rollover.
    // Flush at most once every 30 seconds to avoid unnecessary disk activity.
    private void FlushManagedSessionLedger()
    {
        if (string.IsNullOrWhiteSpace(_currentSessionId)) return;
        var now = DateTimeOffset.Now;
        if (now - _lastSessionLedgerFlush < TimeSpan.FromSeconds(30)) return;
        try
        {
            SessionHistoryStore.Update(_currentSessionId, now,
                WeeklyUsageStore.GetSessionEligibleTime(), GetCurrentSessionApiOutageDuration(now));
            _lastSessionLedgerFlush = now;
        }
        catch (Exception ex) { AppLogger.Warning($"Session ledger update skipped: {ex.Message}"); }
    }

    private void FinalizeManagedSessionLedger(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_currentSessionId)) return;
        try
        {
            SessionHistoryStore.Update(_currentSessionId, now,
                WeeklyUsageStore.GetSessionEligibleTime(), GetCurrentSessionApiOutageDuration(now), finished: true);
        }
        catch (Exception ex) { AppLogger.Warning($"Session ledger finalize failed: {ex.Message}"); }
    }

    private bool IsCurrentlyDropEligible()
    {
        if (!_session.IsTf2Running)
            return false;

        if (_settings.AutoSelectHeavy && (!_autoHeavyReadyForDrops || !_session.HasVerifiedIdle))
            return false;

        if (!_settings.DropNotificationsEnabled)
            return true; // Best-effort when monitoring is deliberately disabled.

        // If Steam's TF2 inventory API is unavailable we cannot know whether a drop
        // has become pending, so pause eligible-time accounting rather than overstating it.
        if (_dropMonitor.IsDegraded)
            return false;

        return _dropMonitor.BaselineReady && _dropMonitor.PendingCount == 0;
    }

    private void ShowWeeklyDetails()
    {
        if (!AccountDataContext.HasActiveAccount)
        {
            MessageBox.Show("No SteamID64 is currently active. Detect/login to Steam first.", "TF2 weekly details", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var now = DateTimeOffset.Now;
        var weekly = DropHistoryStore.GetCurrentWeek(now);
        var usage = WeeklyUsageStore.GetCurrentWeek(now, weekly.DetectedCount);
        var intervalStats = DropHistoryStore.GetCurrentWeekIntervalStats(now, usage.EligibleTime);
        var entries = DropHistoryStore.GetCurrentWeekEntries(now);
        var regularEntries = entries.Where(x => x.CountsTowardWeeklyMetrics).ToList();
        var separateTimerEntries = entries.Where(x => !x.CountsTowardWeeklyMetrics).ToList();
        var recoveredCount = regularEntries.Count(x => x.Recovered);
        var liveCount = Math.Max(0, weekly.DetectedCount - recoveredCount);

        var lines = new List<string>
        {
            $"Steam account: {AccountDataContext.DescribeActiveAccount()}",
            $"Data folder: {AccountDataContext.GetAccountDirectory(AccountDataContext.ActiveSteamId64!)}",
            "",
            $"TF2 week: {weekly.WindowStartUtc:yyyy-MM-dd HH:mm} → {weekly.WindowEndUtc:yyyy-MM-dd HH:mm} UTC",
            $"Regular drops tracked by this manager: {weekly.DetectedCount}",
            $"  Live detections: {liveCount}",
            $"  Recovered pending/log entries: {recoveredCount} (exact original drop time unknown)",
            $"Acknowledged regular drops: {weekly.AcknowledgedCount}",
            $"Separate-timer containers/cases: {separateTimerEntries.Count} (excluded from regular-drop totals/intervals)",
            _dropMonitor.IsDegraded
                ? $"Drop monitor health: DEGRADED — Steam API unavailable ({_dropMonitor.ConsecutiveFailures} failed poll(s)); eligible-time tracking is paused."
                : $"Drop monitor health: healthy{(_dropMonitor.LastSuccessfulCheck is null ? "" : $" — last check {_dropMonitor.LastSuccessfulCheck.Value:HH:mm:ss}")}",
            $"Managed TF2 time: {FormatDuration(usage.ManagedTime)}",
            $"Potentially eligible time tracked this week: {FormatDuration(usage.EligibleTime)}",
            "Community base-time reference: ~10h (not a verified account-specific cap).",
            $"Remaining regular drops: {WeeklyStatisticsPresentation.Remaining(usage)}",
            usage.BasePoolLikelyExhausted
                ? "The ~10h reference was reached. This does NOT prove the drop pool is exhausted."
                : "The reference has not been reached; remaining counts are only estimates.",
            "",
            "Personal eligible-time intervals:"
        };

        if (intervalStats.SampleCount == 0)
        {
            lines.Add("  Not enough v0.4.x interval samples yet. Generic next-drop estimate uses ~30-70 eligible minutes.");
        }
        else
        {
            lines.Add($"  Measured intervals: {intervalStats.SampleCount}");
            lines.Add($"  Average: {FormatMinutes(intervalStats.AverageInterval ?? TimeSpan.Zero)}");
            lines.Add($"  Shortest: {FormatMinutes(intervalStats.ShortestInterval ?? TimeSpan.Zero)}");
            lines.Add($"  Longest: {FormatMinutes(intervalStats.LongestInterval ?? TimeSpan.Zero)}");
        }

        if (intervalStats.EligibleSinceLastDrop is not null)
            lines.Add($"  Eligible since last regular drop: {FormatMinutes(intervalStats.EligibleSinceLastDrop.Value)}");

        lines.Add($"  Next drop: {WeeklyStatisticsPresentation.Next(
            !string.IsNullOrWhiteSpace(_currentSessionId) && _session.IsTf2Running,
            _dropMonitor.IsDegraded, _dropMonitor.PendingCount > 0, usage, intervalStats)}");
        lines.Add("  Intervals are historical observations, not an exact future drop timer.");

        lines.Add("");
        lines.Add("Regular drops recorded this week:");

        if (regularEntries.Count == 0)
        {
            lines.Add("  (none recorded yet)");
        }
        else
        {
            foreach (var entry in regularEntries.Take(12))
            {
                var ack = entry.AcknowledgedAt is null ? "pending" : entry.AcknowledgementRecovered ? "accepted (reconciled)" : "accepted";
                var recovery = entry.Recovered ? " — recovered (exact drop time unknown)" : "";
                lines.Add($"  {entry.DetectedAt.ToLocalTime():ddd HH:mm} — {entry.DisplayName} — {ack}{recovery}");
            }
            if (regularEntries.Count > 12)
                lines.Add($"  ...and {regularEntries.Count - 12} more regular drops in drop-history.json");
        }

        lines.Add("");
        lines.Add("Separate-timer containers/cases (not counted in weekly regular-drop metrics):");
        if (separateTimerEntries.Count == 0)
        {
            lines.Add("  (none recorded this week)");
        }
        else
        {
            foreach (var entry in separateTimerEntries.Take(8))
            {
                var ack = entry.AcknowledgedAt is null ? "pending" : entry.AcknowledgementRecovered ? "accepted (reconciled)" : "accepted";
                lines.Add($"  {entry.DetectedAt.ToLocalTime():ddd HH:mm} — {entry.DisplayName} — {ack}");
            }
            if (separateTimerEntries.Count > 8)
                lines.Add($"  ...and {separateTimerEntries.Count - 8} more container/case entries");
        }

        lines.Add("");
        lines.Add("Current managed session:");
        if (string.IsNullOrWhiteSpace(_currentSessionId))
        {
            lines.Add("  (no managed session active)");
        }
        else
        {
            var sessionEntries = DropHistoryStore.GetSessionEntries(_currentSessionId);
            var sessionRegular = sessionEntries.Count(x => x.CountsTowardWeeklyMetrics);
            var sessionContainers = sessionEntries.Count - sessionRegular;
            var sessionAcceptedRegular = sessionEntries.Count(x => x.CountsTowardWeeklyMetrics && x.AcknowledgedAt is not null);
            var sessionEligible = WeeklyUsageStore.GetSessionEligibleTime();
            lines.Add($"  Eligible: {FormatDuration(sessionEligible)}");
            lines.Add($"  Regular drops: {sessionRegular} ({sessionAcceptedRegular} accepted)");
            lines.Add($"  Containers: {sessionContainers}");
            lines.Add($"  API outage time: {FormatDuration(GetCurrentSessionApiOutageDuration(now))}");
        }

        lines.Add("");
        lines.Add("Measured values come from this manager's local history. Remaining pool and next-drop windows are estimates; Valve does not expose an exact remaining-drop counter.");

        MessageBox.Show(string.Join(Environment.NewLine, lines), "TF2 weekly details", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string FormatMinutes(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        if (value.TotalMinutes < 1)
            return "0m";
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        return $"{Math.Max(0, (int)Math.Round(value.TotalMinutes))}m";
    }

    private static string FormatAgo(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        if (value.TotalMinutes < 1)
            return "just now";
        if (value.TotalHours < 1)
            return $"{(int)value.TotalMinutes}m ago";
        return $"{(int)value.TotalHours}h {value.Minutes}m ago";
    }

    private static string FormatElapsed(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        var totalHours = (int)Math.Floor(value.TotalHours);
        return $"{totalHours:00}:{value.Minutes:00}";
    }

    private string? FindNextBackupServer()
    {
        if (!_settings.TryBackupServersWhenFull)
            return null;
        foreach (var candidate in _settings.ServerProfiles ?? new List<string>())
        {
            var server = Tf2SessionManager.NormalizeServerForSettings(candidate);
            if (server is null || _joinTriedServers.Contains(server))
                continue;
            _joinTriedServers.Add(server);
            return server;
        }
        return null;
    }

    private async Task RecoverJoinConnectionAsync(DateTimeOffset now, string target, string reason)
    {
        // Bounded by the existing watchdog recovery budget. This never creates a
        // new drop baseline, resets account state or starts a second manager session.
        while (_watchdogRecoveries.Count > 0 && now - _watchdogRecoveries.Peek() > TimeSpan.FromMinutes(10))
            _watchdogRecoveries.Dequeue();
        if (_watchdogRecoveries.Count >= 3)
        {
            _watchdogCircuitOpen = true;
            AppendLog("WATCHDOG: recovery budget exceeded (3 attempts/10 min). Paused to avoid a reconnect loop.");
            ShowManagerNotification("TF2 connection retries paused", "Too many server-join attempts. Choose another server and restart the managed session.");
            return;
        }
        _watchdogRecoveries.Enqueue(now);
        if (reason == "same-server retry")
            _sameServerJoinRetries++;
        _watchdogBusy = true;
        _autoHeavyReadyForDrops = false;
        _watchdogStatus.Text = $"Watchdog: {reason}...";
        AppendLog($"WATCHDOG: {reason} -> {target} | attempt={_watchdogRecoveries.Count}/3 in 10m; preserving drop baseline and managed session.");
        WeeklyUsageStore.StopSession(now, eligibleForDrops: false);
        try
        {
            await _session.RecoverAsync(_settings, nextServer: target);
            if (!_session.IsTf2Running)
                throw new InvalidOperationException("TF2 process was not detected after joining another server.");
            WeeklyUsageStore.ResumeSession(DateTimeOffset.Now);
            _joinWarningActive = false;
            _watchdogStatus.Text = $"Watchdog: trying {target}...";
            AppendLog($"WATCHDOG: TF2 relaunched for {target}; waiting for verified Auto Class before counting eligible time.");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Server join recovery failed ({reason}).", ex);
            AppendLog($"WATCHDOG: {reason} failed: {ex.Message}");
            _joinWarningActive = true;
            _watchdogStatus.Text = "Watchdog: server join recovery failed";
            ShowManagerNotification("TF2 server recovery failed", ex.Message);
        }
        finally
        {
            _watchdogBusy = false;
            RefreshSessionUi();
        }
    }

    private async Task CheckWatchdogAsync()
    {
        if (_watchdogBusy || _busy || _watchdogCircuitOpen || string.IsNullOrWhiteSpace(_currentSessionId))
            return;

        var now = DateTimeOffset.Now;
        if (now - _lastWatchdogPoll < TimeSpan.FromSeconds(3))
            return;
        _lastWatchdogPoll = now;

        var health = _session.PollManagedSessionHealth(_settings.ServerJoinTimeoutSeconds);

        // When an idle-server slot is unavailable, the client may stay open and
        // continue waiting. Never count that as idle; retry only at a bounded cadence.
        if (_session.HasVerifiedIdle)
        {
            _nextJoinRetryAt = null;
            _joinWarningActive = false;
        }
        if (health == ManagedSessionHealth.Healthy && _nextJoinRetryAt is DateTimeOffset retryAt)
        {
            if (now < retryAt)
                return;
            _nextJoinRetryAt = null;
            await RecoverJoinConnectionAsync(now, _session.LastRequestedServer ?? _settings.ServerAddress, "same-server retry");
            return;
        }
        if (health == ManagedSessionHealth.Healthy)
            return;

        if (health is ManagedSessionHealth.ServerFull or ManagedSessionHealth.ServerJoinStalled)
        {
            _joinWarningActive = true;
            _autoHeavyReadyForDrops = false;
            var description = health == ManagedSessionHealth.ServerFull
                ? "TF2 reports the server is full"
                : $"TF2 did not confirm a server join within {Math.Clamp(_settings.ServerJoinTimeoutSeconds, 180, 900)} seconds";
            AppendLog($"WATCHDOG: {description}. TF2 is open but NOT idling. Weekly eligible time will not increase.");
            if (!_settings.AutoReconnectEnabled)
            {
                ShowManagerNotification("TF2 not idling — server unavailable", $"{description}. Auto reconnect is disabled. Open TF2 or choose another server.");
                return;
            }

            var backup = FindNextBackupServer();
            if (backup is not null)
            {
                ShowManagerNotification("TF2 server full — switching", $"{description}. Trying saved backup server {backup}.");
                await RecoverJoinConnectionAsync(now, backup, "backup server failover");
                return;
            }
            if (_sameServerJoinRetries >= 3)
            {
                _watchdogCircuitOpen = true;
                _watchdogStatus.Text = "Watchdog: server join paused — 3 retries";
                AppendLog("WATCHDOG: no usable backup or available slot after 3 same-server retries; automatic retries paused. TF2 remains visible/available for manual intervention.");
                ShowManagerNotification("TF2 still not idling", "The server stayed full/unreachable after 3 retry cycles. Automatic recovery paused; select another idle server or restart the session.");
                return;
            }
            var delay = TimeSpan.FromMinutes(Math.Min(8, 2 * (1 << _sameServerJoinRetries)));
            _nextJoinRetryAt = now + delay;
            _watchdogStatus.Text = "Watchdog: no slot — retry planned";
            ShowManagerNotification("TF2 not idling — waiting for slot", $"{description}. No saved backup was enabled/available. Will retry the same server in {delay.TotalMinutes:0} min; you can change it manually using STOP.");
            AppendLog($"WATCHDOG: same-server retry scheduled in {delay.TotalMinutes:0} min; TF2 remains open and Auto Class is still watching for a late server join.");
            return;
        }

        if (health == ManagedSessionHealth.ServerTransition)
        {
            if (!_settings.AutoReconnectEnabled)
            {
                _watchdogStatus.Text = "Watchdog: server/map transition detected";
                AppendLog("WATCHDOG: server/map transition detected inside the existing TF2 process. Auto reconnect is disabled, so Auto Class was not re-run.");
                return;
            }

            _watchdogBusy = true;
            _autoHeavyReadyForDrops = false;
            _watchdogStatus.Text = "Watchdog: server transition → Auto Class...";
            AppendLog("WATCHDOG: server/map transition detected while TF2 stayed open. Re-running Auto Class in the SAME process; drop monitor baseline/history are preserved.");

            try
            {
                var rearmed = await _session.ReRunAutoHeavyForServerTransitionAsync(_settings);
                if (!rearmed)
                {
                    _watchdogStatus.Text = "Watchdog: transition re-arm failed";
                    AppendLog("WATCHDOG: could not re-arm Auto Class for the map transition.");
                    return;
                }

                _postTransitionRecheckPending = true;
                _watchdogStatus.Text = "Watchdog: map transition — waiting for join";
                AppendLog("WATCHDOG: in-process map transition re-armed. Waiting for verified Auto Class; watchdog remains active during a full-server queue.");
            }
            finally
            {
                _watchdogBusy = false;
                RefreshSessionUi();
            }

            return;
        }

        _nextJoinRetryAt = null;
        var reason = health == ManagedSessionHealth.ProcessExited ? "TF2 process exited" : "server disconnect detected";
        if (!_settings.AutoReconnectEnabled)
        {
            _watchdogStatus.Text = $"Watchdog: {reason}";
            return;
        }

        while (_watchdogRecoveries.Count > 0 && now - _watchdogRecoveries.Peek() > TimeSpan.FromMinutes(10))
            _watchdogRecoveries.Dequeue();

        if (_watchdogRecoveries.Count >= 3)
        {
            _watchdogCircuitOpen = true;
            _watchdogStatus.Text = "Watchdog: paused — 3 recoveries/10m";
            AppendLog("WATCHDOG paused automatic recovery after 3 attempts within 10 minutes. Use STOP, inspect the server/network, then START IDLE again.");
            ShowManagerNotification("TF2 watchdog paused", "3 recovery attempts occurred within 10 minutes. Use STOP, inspect the connection/server, then START IDLE again.");
            return;
        }

        _watchdogBusy = true;
        _watchdogRecoveries.Enqueue(now);
        _autoHeavyReadyForDrops = false;
        _watchdogStatus.Text = "Watchdog: recovering...";
        AppendLog($"WATCHDOG: {reason}. Restarting TF2 in 5 seconds; drop-monitor baseline/history are preserved.");

        WeeklyUsageStore.StopSession(now, eligibleForDrops: false);

        try
        {
            await Task.Delay(5000);
            // Preserve whichever server actually succeeded, including a previously
            // selected backup; do not silently return to a full primary server.
            await _session.RecoverAsync(_settings, nextServer: _session.LastRequestedServer);

            if (!_session.IsTf2Running)
                throw new InvalidOperationException("TF2 was not detected after watchdog recovery launch.");

            WeeklyUsageStore.ResumeSession(DateTimeOffset.Now);
            _watchdogStatus.Text = "Watchdog: recovered; Auto Class running...";
            AppendLog("WATCHDOG: TF2 relaunched successfully. Auto Class will re-enter the server/class flow; drop monitoring continued without rebuilding its baseline.");
            ShowManagerNotification("TF2 recovered", "TF2 was relaunched automatically. Auto Class is re-entering the idle server.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Watchdog recovery failed.", ex);
            _watchdogStatus.Text = "Watchdog: recovery failed";
            AppendLog($"WATCHDOG recovery failed: {ex.Message}");
        }
        finally
        {
            _watchdogBusy = false;
            RefreshSessionUi();
        }
    }

    private void OnDropMonitorAvailabilityChanged(DropMonitorAvailabilityEvent availability)
    {
        if (IsDisposed)
            return;

        void Apply()
        {
            if (!availability.Available)
            {
                _dropMonitorWasDegraded = true;
                if (!string.IsNullOrWhiteSpace(_currentSessionId) && _sessionApiOutageStartedAt is null)
                    _sessionApiOutageStartedAt = availability.ChangedAt;
                _dropStatus.Text = "⚠ DROP ALERTS DEGRADED — Steam API unavailable";
                _dropStatus.ForeColor = Color.DarkOrange;
                AppendLog("⚠ DROP MONITOR DEGRADED: Steam's TF2 inventory API is unavailable. TF2 can still receive drops, but this app cannot confirm a new pending item until Steam recovers.");
                AppendLog("Eligible-time tracking is paused conservatively during the outage; the monitor keeps retrying and preserves its baseline/history.");
                if (_settings.ShowApiStatusNotifications) ShowManagerNotification(
                    "TF2 drop alerts temporarily unavailable",
                    "Steam's TF2 inventory API is unavailable. TF2 can still receive drops. The manager is retrying automatically and has paused eligible-time tracking."
                );
                RefreshWeeklyUi();
                return;
            }

            if (_dropMonitorWasDegraded)
            {
                _dropMonitorWasDegraded = false;
                if (_sessionApiOutageStartedAt is DateTimeOffset outageStart)
                {
                    if (availability.ChangedAt > outageStart)
                        _sessionApiOutageAccumulated += availability.ChangedAt - outageStart;
                    _sessionApiOutageStartedAt = null;
                }
                _dropStatus.ForeColor = SystemColors.ControlText;
                var outageText = availability.OutageStartedAt is null
                    ? "after a temporary outage"
                    : $"after {FormatAgo(availability.ChangedAt - availability.OutageStartedAt.Value).Replace(" ago", "")}";
                AppendLog($"✓ DROP MONITOR RECOVERED {outageText}. Pending items will be reconciled against the preserved pre-outage baseline.");
                if (_settings.ShowApiStatusNotifications) ShowManagerNotification(
                    "TF2 drop monitor recovered",
                    "Steam's TF2 inventory API is responding again. Pending items are being reconciled now."
                );
                RefreshWeeklyUi();
            }
        }

        if (InvokeRequired)
            BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private void OnDropBaselineEstablished(DropBaselineEvent baseline)
    {
        if (IsDisposed)
            return;

        void Apply()
        {
            var reconciledAccepted = DropHistoryStore.ReconcileAcknowledgementState(baseline.Snapshot, baseline.Snapshot.CheckedAt);
            if (reconciledAccepted.Count > 0)
            {
                AppendLog($"↺ BASELINE ACK STATE RECONCILED: {reconciledAccepted.Count} previously tracked item(s) are no longer pending and were marked accepted (exact accept time unknown).");
            }

            var recoveredItems = new List<PendingDropItem>();
            var reason = baseline.RecoveredAfterApiOutage
                ? "baseline-after-api-outage"
                : "baseline-at-session-start";

            foreach (var item in baseline.Snapshot.PendingItems)
            {
                if (DropHistoryStore.RecordRecoveredPending(
                        item,
                        baseline.Snapshot.CheckedAt,
                        _currentSessionId,
                        _session.LastRequestedServer,
                        reason))
                {
                    recoveredItems.Add(item);
                }
            }

            if (recoveredItems.Count == 0)
                return;

            var regularRecovered = recoveredItems.Count(x => x.CountsTowardWeeklyMetrics);
            var containerRecovered = recoveredItems.Count - regularRecovered;

            foreach (var item in recoveredItems)
            {
                var metricNote = item.CountsTowardWeeklyMetrics
                    ? "counts toward regular weekly metrics"
                    : "separate-timer container; excluded from regular weekly metrics";
                AppendLog($"↺ RECOVERED PENDING ITEM: {item.DisplayName} | item={item.ItemId} | defindex={item.DefIndex} | {metricNote} | exact drop time unknown");
            }

            _lastDropItem = recoveredItems.Count == 1
                ? recoveredItems[0].DisplayName
                : $"{recoveredItems.Count} recovered pending items";
            _dropStatus.Text = recoveredItems.Count == 1
                ? $"↺ RECOVERED PENDING: {recoveredItems[0].DisplayName}"
                : $"↺ RECOVERED PENDING: {recoveredItems.Count} items";

            var outageText = baseline.RecoveredAfterApiOutage ? " after the Steam API recovered" : " at session startup";
            ShowManagerNotification(
                "Recovered pending TF2 item",
                $"{recoveredItems.Count} pending item(s) were first seen{outageText}. Regular weekly drops recovered: {regularRecovered}; separate-timer containers excluded: {containerRecovered}."
            );
            RefreshWeeklyUi();
        }

        if (InvokeRequired)
            BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private void OnDropMonitorStatusChanged(string status)
    {
        if (IsDisposed)
            return;

        void Apply()
        {
            if (_lastDropItem is not null && status.Contains("pending", StringComparison.OrdinalIgnoreCase))
                return;

            _dropStatus.Text = status;
            if (status.Equals("Drop monitor: off", StringComparison.OrdinalIgnoreCase))
            {
                _lastDropItem = null;
                _dropStatus.ForeColor = SystemColors.ControlText;
            }
        }

        if (InvokeRequired)
            BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private async Task RecheckDropsAfterTransitionAsync()
    {
        try
        {
            AppendLog("DROP MONITOR: immediate check after verified map-transition re-entry...");
            var snapshot = await _dropMonitor.RefreshNowAsync();
            AppendLog($"DROP MONITOR: post-transition check OK | pending={snapshot.PendingCount}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Post-transition drop reconciliation unavailable: {ex.Message}");
            AppendLog("DROP MONITOR: immediate check unavailable; background polling continues.");
        }
    }

    private void OnAutoHeavyStatusChanged(string status)
    {
        if (IsDisposed)
            return;

        void Apply()
        {
            _autoHeavyStatus.Text = status;
            if (status.Contains(" selected ✓", StringComparison.OrdinalIgnoreCase))
            {
                _autoHeavyReadyForDrops = true;
                _joinWarningActive = false;
                _nextJoinRetryAt = null;
                _sameServerJoinRetries = 0;
                _joinTriedServers.Clear();
                if (!string.IsNullOrWhiteSpace(_session.LastRequestedServer))
                    _joinTriedServers.Add(_session.LastRequestedServer);
                if (_postTransitionRecheckPending)
                {
                    _postTransitionRecheckPending = false;
                    if (_dropMonitor.IsRunning)
                        _ = RecheckDropsAfterTransitionAsync();
                }
            }
            AppendLog(status);
        }

        if (InvokeRequired)
            BeginInvoke((Action)Apply);
        else
            Apply();
    }

    // Every routine app alert uses an app-owned toast so the user, not Windows,
    // controls its screen corner. Windows-owned notification balloons cannot do this.
    private void ShowManagerNotification(string title, string text)
    {
        try
        {
            ShowPositionedToast(new ManagerToastForm(title, text, _settings.DarkMode));
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not show manager notification: {ex.Message}");
        }
    }

    private void ShowDropNotification(PendingDropItem item)
    {
        try
        {
            SystemSounds.Exclamation.Play();
            // Newest notice for an item replaces an earlier drop toast, without
            // removing manager/API/watchdog notifications that may be visible.
            if (_activeToast is { IsDisposed: false }) _activeToast.Close();
            var toast = new DropToastForm(item, _settings.DarkMode, () => _session.BringTf2ToFront());
            _activeToast = toast;
            ShowPositionedToast(toast);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not show image drop alert: {ex.Message}");
        }
    }

    private void ShowPositionedToast(Form toast)
    {
        if (IsDisposed || Disposing) { toast.Dispose(); return; }
        toast.StartPosition = FormStartPosition.Manual;
        toast.FormClosed += (_, _) =>
        {
            _toastWindows.Remove(toast);
            if (!IsDisposed && !Disposing) RepositionToasts();
        };
        _toastWindows.Add(toast);
        RepositionToasts();
        toast.Show(); // App-owned toasts use ShowWithoutActivation to preserve TF2 focus.
        // Re-apply after the native window is created; same placement as Preview.
        RepositionToasts();
        var activeScreen = Screen.FromControl(this);
        AppLogger.Info($"NOTIFICATION POSITION | corner={ToastPlacement.Normalize(_settings.NotificationCorner)} | monitor={activeScreen.DeviceName} | actual={toast.Left},{toast.Top}");
    }

    private void RepositionToasts()
    {
        if (IsDisposed || Disposing) return;
        var area = Screen.FromControl(this).WorkingArea;
        int offset = 0;
        // Newest alert sits closest to the selected corner, with older alerts stacked
        // above/below. Retire older alerts when screen height would be exceeded.
        for (int i = _toastWindows.Count - 1; i >= 0; i--)
        {
            var toast = _toastWindows[i];
            if (toast.IsDisposed) { _toastWindows.RemoveAt(i); continue; }
            if (offset > 0 && offset + toast.Height + 32 > area.Height)
            {
                toast.Close();
                continue;
            }
            ToastPlacement.Apply(toast, area, _settings.NotificationCorner, offset);
            offset += toast.Height + 9;
        }
    }

    private void CheckRepeatedAlerts()
    {
        if (!_settings.RepeatDropAlerts || _dropMonitor.IsDegraded || !(_dropMonitor.IsRunning)) return;
        var now = DateTimeOffset.Now;
        foreach (var entry in _reminders.ToArray())
        {
            var reminder = entry.Value;
            if (now < reminder.Next) continue;
            if (!_settings.RepeatUntilAcknowledged && reminder.Sent >= Math.Clamp(_settings.RepeatCount, 1, 100))
            {
                _reminders.Remove(entry.Key);
                continue;
            }
            ShowDropNotification(reminder.Item);
            var sent = reminder.Sent + 1;
            if (!_settings.RepeatUntilAcknowledged && sent >= _settings.RepeatCount)
                _reminders.Remove(entry.Key);
            else
                _reminders[entry.Key] = (reminder.Item, sent, now.AddSeconds(Math.Clamp(_settings.RepeatIntervalSeconds, 5, 3600)));
        }
    }

    private void OpenPreferences()
    {
        using var dialog = new PreferencesDialog(_settings);
        ThemeHelper.Apply(dialog, _settings.DarkMode);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ThemeHelper.Apply(this, _settings.DarkMode);
            ApplyDashboardTheme();
            RepositionToasts();
            RefreshServerProfiles();
            AppendLog("Preferences saved. Secondary API key changes take effect on the next inventory check/session.");
        }
    }

    private void RefreshServerProfiles()
    {
        _savedServerCombo.Items.Clear();
        _savedServerCombo.Items.Add("Saved servers...");
        foreach (var server in _settings.ServerProfiles ?? new List<string>())
            _savedServerCombo.Items.Add(server);
        _savedServerCombo.SelectedIndex = 0;
    }

    private void OpenHistory()
    {
        if (!AccountDataContext.HasActiveAccount) { MessageBox.Show("No Steam account active."); return; }
        var rows = DropHistoryStore.GetAllEntries();
        using var dialog = new Form
        {
            Text = $"Drop history — {AccountDataContext.ActiveSteamId64}",
            Width = 950, Height = 530, StartPosition = FormStartPosition.CenterParent
        };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
        grid.DataSource = rows.Select(x => new
        {
            x.DisplayName,
            Category = x.CountsTowardWeeklyMetrics ? "Regular" : "Container",
            Detected = x.DetectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Accepted = x.AcknowledgedAt is not null ? "Yes" : "Pending",
            x.ItemId,
            x.SessionId,
            x.Server
        }).ToList();
        dialog.Controls.Add(grid);
        ThemeHelper.Apply(dialog, _settings.DarkMode);
        dialog.ShowDialog(this);
    }

    private void ShowTestNotification()
    {
        // The test uses the same custom notification as a real pending drop,
        // without ever writing to history or starting a reminder.
        ShowDropNotification(new PendingDropItem(0, 0, 0, 0, "Test notification (no actual drop)"));
        AppendLog("Image-alert test displayed; no drop was recorded.");
    }

    private TimeSpan GetCurrentSessionApiOutageDuration(DateTimeOffset now)
    {
        var total = _sessionApiOutageAccumulated;
        if (_sessionApiOutageStartedAt is DateTimeOffset start && now > start)
            total += now - start;
        return total < TimeSpan.Zero ? TimeSpan.Zero : total;
    }

    private bool ActivateAccountContext(SteamDetection detection)
    {
        if (!AccountDataContext.IsValidSteamId64(detection.SteamId64))
        {
            var hadAccount = AccountDataContext.HasActiveAccount;
            AccountDataContext.Clear();
            return hadAccount;
        }

        return AccountDataContext.Activate(detection.SteamId64, detection.PersonaName);
    }

    private void EnsureFreshSteamAccountContext()
    {
        if (!string.IsNullOrWhiteSpace(_currentSessionId))
            return;

        var fresh = SteamLocator.Detect();
        var oldSteamId = _detection.SteamId64;
        if (!string.Equals(fresh.SteamId64, oldSteamId, StringComparison.Ordinal) ||
            !string.Equals(fresh.PersonaName, _detection.PersonaName, StringComparison.Ordinal))
        {
            _detection = fresh;
            _session.UpdateDetection(_detection);
            var changed = ActivateAccountContext(_detection);
            RefreshDetectionUi();

            if (changed)
            {
                _lastDropItem = null;
                _dropMonitor.Stop();
                _dropMonitorWasDegraded = false;
                AppendLog($"ACCOUNT SWITCH AUTO-DETECTED before action: {oldSteamId ?? "none"} -> {_detection.SteamId64 ?? "unavailable"}. Loaded isolated account data.");
                ReconcileCurrentAccountHistory();
            }
        }
    }

    private void ReconcileCurrentAccountHistory()
    {
        if (!AccountDataContext.HasActiveAccount)
            return;

        var reconciledHistory = DropHistoryStore.ReconcileMissingAcknowledgementsFromLogs(DateTimeOffset.Now);
        if (reconciledHistory.Count == 0)
        {
            RefreshWeeklyUi();
            return;
        }

        AppendLog($"↺ HISTORY RECONCILED [{AccountDataContext.ActiveSteamId64}]: restored {reconciledHistory.Count} account-scoped acknowledged drop(s) from retained v0.4.5+ logs.");
        foreach (var entry in reconciledHistory.Take(6))
        {
            var acceptedText = entry.AcknowledgedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown";
            AppendLog($"  ↺ {entry.DisplayName} | item={entry.ItemId} | accepted={acceptedText}");
        }
        RefreshWeeklyUi();
    }

    private async Task ReconcileActiveAccountWithApiAsync(string reason)
    {
        if (!AccountDataContext.HasActiveAccount || !string.IsNullOrWhiteSpace(_currentSessionId))
            return;
        if (string.IsNullOrWhiteSpace(_apiKeyBox.Text) || string.IsNullOrWhiteSpace(_detection.SteamId64))
            return;

        try
        {
            var snapshot = await _dropMonitor.CheckNowAsync(_apiKeyBox.Text, _detection.SteamId64!);

            var recoveredPending = 0;
            foreach (var item in snapshot.PendingItems)
            {
                if (DropHistoryStore.RecordRecoveredPending(
                        item,
                        snapshot.CheckedAt,
                        sessionId: null,
                        server: null,
                        recoveryReason: $"account-context-{reason.Replace(' ', '-') }"))
                {
                    recoveredPending++;
                }
            }

            var reconciledAccepted = DropHistoryStore.ReconcileAcknowledgementState(snapshot, snapshot.CheckedAt);
            if (recoveredPending > 0 || reconciledAccepted.Count > 0)
            {
                AppendLog($"↺ ACCOUNT STATE RECONCILED ({reason}) | pending restored={recoveredPending} | acknowledgements restored={reconciledAccepted.Count}");
                foreach (var entry in reconciledAccepted.Take(5))
                    AppendLog($"  ✓ accepted while account was not monitored: {entry.DisplayName} | item={entry.ItemId} | exact accept time unknown");
            }

            RefreshWeeklyUi();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Account-state API reconciliation skipped ({reason}): {ex.Message}");
            AppendLog($"Account-state reconciliation deferred ({reason}): Steam API unavailable or returned an error. It will be retried on Check now / next session.");
        }
    }

    private string RequireSteamId()
    {
        if (string.IsNullOrWhiteSpace(_detection.SteamId64))
            throw new InvalidOperationException("SteamID64 was not detected. Click 'Detect again' with Steam logged in.");
        return _detection.SteamId64!;
    }

    private void OnLogLine(string line)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
            BeginInvoke(new Action<string>(AppendLog), line);
        else
            AppendLog(line);
    }

    private void AppendLog(string text)
    {
        if (_logBox.IsDisposed)
            return;

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not open folder '{path}': {ex.Message}");
        }
    }
}
