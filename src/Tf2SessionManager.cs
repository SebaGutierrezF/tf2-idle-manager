using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TF2IdleManager;

public sealed partial class Tf2SessionManager
{
    private const string IdleCfgName = "tf2_idle_manager.cfg";

    private SteamDetection _detection;
    private Process? _trackedProcess;
    private CancellationTokenSource? _autoHeavyCts;
    private Task? _autoHeavyTask;
    private long _consoleStartOffset;
    private long _healthConsoleOffset;
    private long _pendingTransitionStartOffset;
    private bool _serverTransitionWatchArmed;
    private volatile bool _joinConfirmed;
    private volatile bool _expectAutoClass;
    private long _joinStartedUtcMilliseconds;
    private int _joinStallReported;
    public bool IsServerJoinPending => _expectAutoClass && !_joinConfirmed && IsTf2Running;
    public bool HasVerifiedIdle => _joinConfirmed && IsTf2Running;

    public event Action<string>? AutoHeavyStatusChanged;

    public DateTimeOffset? StartedAt { get; private set; }
    public string? LastRequestedServer { get; private set; }

    public Tf2SessionManager(SteamDetection detection)
    {
        _detection = detection;
        CleanupLegacyDropSentinel();
    }

    public void UpdateDetection(SteamDetection detection)
    {
        _detection = detection;
        CleanupLegacyDropSentinel();
    }

    public bool IsTf2Running => FindTf2Process() is not null;

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default, string? serverOverride = null)
    {
        if (!_detection.Ready || string.IsNullOrWhiteSpace(_detection.SteamExe) || string.IsNullOrWhiteSpace(_detection.Tf2CfgDirectory))
            throw new InvalidOperationException("Steam or Team Fortress 2 could not be detected.");

        var server = NormalizeServer(serverOverride ?? settings.ServerAddress);
        if (server is null)
            throw new InvalidOperationException("Enter a server as hostname:port or IP:port.");

        if (IsTf2Running)
            throw new InvalidOperationException("Team Fortress 2 is already running. Stop it before starting a managed idle session.");

        CancelAutoHeavyWorker();
        Directory.CreateDirectory(_detection.Tf2CfgDirectory);
        CleanupLegacyDropSentinel();

        WriteIdleCfg(_detection.Tf2CfgDirectory, settings);
        _consoleStartOffset = GetConsoleLogLength();
        _healthConsoleOffset = _consoleStartOffset;
        _pendingTransitionStartOffset = 0;
        _serverTransitionWatchArmed = false;
        _joinConfirmed = false;
        _expectAutoClass = settings.AutoSelectHeavy;
        Interlocked.Exchange(ref _joinStallReported, 0);
        Interlocked.Exchange(ref _joinStartedUtcMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var psi = new ProcessStartInfo
        {
            FileName = _detection.SteamExe,
            UseShellExecute = false,
            WorkingDirectory = _detection.SteamDirectory ?? Path.GetDirectoryName(_detection.SteamExe) ?? Environment.CurrentDirectory
        };

        psi.ArgumentList.Add("-applaunch");
        psi.ArgumentList.Add("440");
        psi.ArgumentList.Add("-novid");
        psi.ArgumentList.Add("-windowed");
        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add(Math.Clamp(settings.WindowWidth, 320, 3840).ToString());
        psi.ArgumentList.Add("-h");
        psi.ArgumentList.Add(Math.Clamp(settings.WindowHeight, 240, 2160).ToString());
        psi.ArgumentList.Add("-console");
        psi.ArgumentList.Add("-condebug");

        psi.ArgumentList.Add("+connect");
        psi.ArgumentList.Add(server);
        psi.ArgumentList.Add("+exec");
        psi.ArgumentList.Add(IdleCfgName);

        foreach (var option in SplitLaunchOptions(settings.ExtraLaunchOptions))
            psi.ArgumentList.Add(option);

        AppLogger.Info(
            $"Launching TF2 | server={server} | attack={settings.AttackEnabled} | left={settings.LeftEnabled} | "
            + $"autoClass={settings.EffectiveClass}"
        );
        Process.Start(psi);

        LastRequestedServer = server;
        StartedAt = DateTimeOffset.Now;

        _trackedProcess = await WaitForTf2ProcessAsync(TimeSpan.FromSeconds(60), cancellationToken);
        if (_trackedProcess is null)
        {
            StartedAt = null;
            LastRequestedServer = null;
            AppLogger.Warning("Steam launch was requested, but the TF2 process was not detected within 60 seconds.");
            return;
        }

        AppLogger.Info($"TF2 process detected | pid={_trackedProcess.Id}");

        if (settings.AutoSelectHeavy)
        {
            AutoHeavyStatusChanged?.Invoke("Auto Class: waiting for player join...");
            AppLogger.Info("Auto Class armed. Waiting for the local player connection line, then reproducing the confirmed UI sequence with corrected Win32 keyboard injection: Enter x2 -> 1 (random team) -> selected class.");
            if (settings.MinimizeTf2AfterLaunch)
                AppLogger.Info("TF2 minimization deferred until Auto Class finishes.");

            _autoHeavyCts = new CancellationTokenSource();
            _autoHeavyTask = Task.Run(() => RunAutoHeavyAsync(settings, _consoleStartOffset, _autoHeavyCts.Token));
        }
        else if (settings.MinimizeTf2AfterLaunch)
        {
            await Task.Delay(1500, cancellationToken);
            MinimizeTf2();
        }
    }

    public ManagedSessionHealth PollManagedSessionHealth(int joinTimeoutSeconds = 240)
    {
        if (!IsTf2Running)
            return ManagedSessionHealth.ProcessExited;

        var chunkStartOffset = _healthConsoleOffset;
        var chunk = ReadNewConsoleText(ref _healthConsoleOffset);
        // A running tf_win64.exe does not mean the player joined a server.
        // Connection attempts that never produce a verified class-selection marker
        // are explicitly monitored, including the case where the console is silent.
        var serverFull = chunk.Contains("Server is full", StringComparison.OrdinalIgnoreCase)
            || chunk.Contains("ServerRejectServerFull", StringComparison.OrdinalIgnoreCase)
            || chunk.Contains("NETWORK_DISCONNECT_SERVERFULL", StringComparison.OrdinalIgnoreCase);
        if (IsServerJoinPending && Interlocked.CompareExchange(ref _joinStallReported, 1, 0) == 0)
        {
            var start = Interlocked.Read(ref _joinStartedUtcMilliseconds);
            var exceeded = start > 0
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - start
                    >= Math.Clamp(joinTimeoutSeconds, 180, 900) * 1000L;
            if (serverFull || exceeded)
            {
                var issue = serverFull ? ManagedSessionHealth.ServerFull : ManagedSessionHealth.ServerJoinStalled;
                AppLogger.Warning($"WATCHDOG: player has not entered/selected a class | reason={issue} | target={LastRequestedServer} | joinConfirmed=false");
                return issue;
            }
            // This compare/exchange is only intended to latch an actual warning.
            Interlocked.Exchange(ref _joinStallReported, 0);
        }

        if (string.IsNullOrWhiteSpace(chunk))
            return ManagedSessionHealth.Healthy;

        var disconnectMarkers = new[]
        {
            "Dropped from server",
            "Connection failed after",
            "Server connection timed out",
            "Connection timed out",
            "Lost connection to server",
            "Host_Error: Disconnected",
            "Server shutting down",
            "Disconnect: Server",
            "Disconnect:",
            "CL:  Disconnecting"
        };

        var lastDisconnect = -1;
        foreach (var marker in disconnectMarkers)
            lastDisconnect = Math.Max(lastDisconnect, chunk.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase));

        // A server restart or map transition can keep tf_win64.exe alive while TF2 sends the
        // client back through the welcome/team/class screens. Once the initial Auto Class flow
        // has completed, a fresh server_spawn/Map marker means the same process entered a new
        // server lifecycle and Auto Class must be re-armed without restarting TF2.
        var lastTransition = -1;
        if (_serverTransitionWatchArmed)
        {
            foreach (var marker in new[] { "Client reached server_spawn." })
                lastTransition = Math.Max(lastTransition, chunk.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase));
        }

        // If a disconnect and a later server_spawn arrive in the same watchdog chunk, the client
        // has already reconnected by itself. Prefer the transition recovery over killing TF2.
        if (_serverTransitionWatchArmed && lastTransition >= 0 && lastTransition > lastDisconnect)
        {
            _pendingTransitionStartOffset = chunkStartOffset;
            _serverTransitionWatchArmed = false;
            _joinConfirmed = false;
            Interlocked.Exchange(ref _joinStallReported, 0);
            Interlocked.Exchange(ref _joinStartedUtcMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            AppLogger.Warning("WATCHDOG detected a server/map transition inside the existing TF2 process. Auto Class must be re-armed.");
            return ManagedSessionHealth.ServerTransition;
        }

        if (lastDisconnect >= 0)
        {
            // A rejection before joining is different from a drop from a live
            // idle session. Use the bounded join recovery, not the crash loop.
            if (IsServerJoinPending)
            {
                if (Interlocked.CompareExchange(ref _joinStallReported, 1, 0) == 0)
                {
                    AppLogger.Warning($"WATCHDOG: connect attempt rejected before class selection | target={LastRequestedServer}");
                    return ManagedSessionHealth.ServerJoinStalled;
                }
                return ManagedSessionHealth.Healthy;
            }
            _joinConfirmed = false;
            return ManagedSessionHealth.Disconnected;
        }
        return ManagedSessionHealth.Healthy;
    }

    public Task<bool> ReRunAutoHeavyForServerTransitionAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!IsTf2Running)
            return Task.FromResult(false);
        CancelAutoHeavyWorker();
        _serverTransitionWatchArmed = false;
        _joinConfirmed = false;
        _expectAutoClass = settings.AutoSelectHeavy;
        Interlocked.Exchange(ref _joinStallReported, 0);
        Interlocked.Exchange(ref _joinStartedUtcMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var startOffset = _pendingTransitionStartOffset > 0
            ? _pendingTransitionStartOffset
            : GetConsoleLogLength();
        _pendingTransitionStartOffset = 0;

        AppLogger.Info($"AUTO CLASS re-armed for in-process server/map transition | startOffset={startOffset}");
        AutoHeavyStatusChanged?.Invoke("Auto Class: server transition detected; waiting for player join...");

        _autoHeavyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = Task.Run(() => RunAutoHeavyAsync(settings, startOffset, _autoHeavyCts.Token));
        _autoHeavyTask = worker;
        // Do not block the watchdog while waiting in a full-server lobby.
        // The worker continues watching; an independent watchdog poll can
        // detect a failed/slow join and select a backup server.
        return Task.FromResult(true);
    }

    public async Task RecoverAsync(AppSettings settings, CancellationToken cancellationToken = default, string? nextServer = null)
    {
        AppLogger.Warning("WATCHDOG recovery requested. Restarting TF2 while preserving the current manager/drop-monitor session.");
        CancelAutoHeavyWorker();
        await ForceCloseTf2Async();
        await Task.Delay(1500, cancellationToken);
        await StartAsync(settings, cancellationToken, nextServer);
    }

    public async Task StopAsync(AppSettings settings)
    {
        CancelAutoHeavyWorker();
        _joinConfirmed = false;
        _expectAutoClass = false;

        var process = FindTf2Process();
        if (process is null)
        {
            StartedAt = null;
            LastRequestedServer = null;
            _trackedProcess = null;
            AppLogger.Info("STOP requested; TF2 was not running.");
            return;
        }

        AppLogger.Info($"STOP requested | pid={process.Id} | closeTf2={settings.CloseTf2OnStop}");

        if (!settings.CloseTf2OnStop)
        {
            BringTf2ToFront();
            StartedAt = null;
            LastRequestedServer = null;
            AppLogger.Warning("Close TF2 on STOP is disabled; TF2 remains open.");
            return;
        }

        try
        {
            if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
            {
                process.CloseMainWindow();
                var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(5));
                if (!exited && !process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            else if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not close TF2 cleanly; attempting forced termination.", ex);
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx)
            {
                AppLogger.Error("Forced TF2 termination failed.", killEx);
                throw;
            }
        }
        finally
        {
            await Task.Delay(500);
            StartedAt = null;
            LastRequestedServer = null;
            _trackedProcess = null;
        }
    }

    public void CleanupOnAppExit()
    {
        CancelAutoHeavyWorker();
    }

    public bool BringTf2ToFront()
    {
        var process = FindTf2Process();
        if (process is null)
            return false;

        try
        {
            var handle = GetTf2InputWindow(process);
            if (handle == IntPtr.Zero)
                return false;

            ShowWindowAsync(handle, SwRestore);
            SetForegroundWindow(handle);
            return IsTf2Foreground(process.Id);
        }
        catch
        {
            return false;
        }
    }

    public bool MinimizeTf2()
    {
        var process = FindTf2Process();
        if (process is null)
            return false;

        try
        {
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
                return false;

            ShowWindow(handle, 6); // SW_MINIMIZE
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RunAutoHeavyAsync(AppSettings settings, long startOffset, CancellationToken cancellationToken)
    {
        var readySeen = false;
        // Use a random 1-9 class key when Random is selected. Key 0 can act as
        // Back in some TF2 class menus, so never rely on it for random selection.
        var configuredClass = settings.EffectiveClass;
        var className = configuredClass == "Random"
            ? Tf2Classes.Names[Random.Shared.Next(1, 10)]
            : configuredClass;
        if (configuredClass == "Random")
            AppLogger.Info($"AUTO CLASS: Random chose {className} for this join cycle.");
        var classKey = Tf2Classes.Key(className);
        var classCfg = Tf2Classes.Cfg(className);
        try
        {
            var playerName = (_detection.PersonaName ?? string.Empty).Trim();
            readySeen = await WaitForLocalPlayerReadyAsync(
                playerName,
                startOffset,
                TimeSpan.FromHours(12),
                cancellationToken
            );

            if (!readySeen)
            {
                AppLogger.Warning("Auto Class timed out waiting for the local player's server connection line.");
                AutoHeavyStatusChanged?.Invoke("Auto Class: player join timeout");
                return false;
            }

            AppLogger.Info("AUTO CLASS: local player connection detected. Waiting briefly for the welcome/MOTD UI to become interactive.");
            AutoHeavyStatusChanged?.Invoke("Auto Class: player joined; waiting for menus...");

            await Task.Delay(1800, cancellationToken);

            // TF2 may create/recreate its render window during server load. Foreground
            // activation is restricted by Windows, so never send game keys without
            // verifying that an actual TF2 window owns the keyboard focus.
            if (!await FocusTf2ForInputAsync(TimeSpan.FromSeconds(15), cancellationToken))
            {
                AppLogger.Warning("AUTO CLASS: could not focus TF2 after retrying window activation. No join keys were sent.");
                AutoHeavyStatusChanged?.Invoke("Auto Class: could not focus TF2");
                return false;
            }

            AppLogger.Info($"AUTO CLASS: TF2 focus verified | Win32 INPUT size={Marshal.SizeOf<INPUT>()} bytes | process64={Environment.Is64BitProcess}. Starting confirmed menu flow.");

            AutoHeavyStatusChanged?.Invoke("Auto Class: accepting menu 1/2...");
            if (!await SendAutoClassKeyAsync(VkReturn, "ENTER #1", cancellationToken))
                return AutoClassKeyDeliveryFailed("ENTER #1");
            await Task.Delay(1200, cancellationToken);

            AutoHeavyStatusChanged?.Invoke("Auto Class: accepting menu 2/2...");
            if (!await SendAutoClassKeyAsync(VkReturn, "ENTER #2", cancellationToken))
                return AutoClassKeyDeliveryFailed("ENTER #2");
            await Task.Delay(1500, cancellationToken);

            // The TF2 menu labels Random team as 1 (not Space).
            AutoHeavyStatusChanged?.Invoke("Auto Class: choosing random team (1)...");
            if (!await SendAutoClassKeyAsync(Vk1, "TEAM 1", cancellationToken))
                return AutoClassKeyDeliveryFailed("TEAM 1");
            AppLogger.Info("AUTO CLASS: sent ENTER -> ENTER -> 1 (Random team).");
            await Task.Delay(1900, cancellationToken);

            var heavyCheckOffset = GetConsoleLogLength();
            var heavyVerified = false;

            for (var attempt = 1; attempt <= 4 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                // TF2 class menu uses 1-9 for classes. Random preselects one of those keys.
                AutoHeavyStatusChanged?.Invoke($"Auto Class: selecting {className} {attempt}/4...");
                if (!await SendAutoClassKeyAsync(classKey, $"CLASS {className} attempt {attempt}/4", cancellationToken))
                    return AutoClassKeyDeliveryFailed($"CLASS {className} attempt {attempt}/4");

                AppLogger.Info($"AUTO CLASS attempt {attempt}/4: sent class key 0x{classKey:X2} for {className}.");
                heavyVerified = string.IsNullOrWhiteSpace(classCfg)
                    ? await WaitForAnyConsoleTextAsync(
                        new[] { "scout.cfg", "soldier.cfg", "pyro.cfg", "demoman.cfg", "heavyweapons.cfg", "engineer.cfg", "medic.cfg", "sniper.cfg", "spy.cfg" },
                        heavyCheckOffset, TimeSpan.FromMilliseconds(2500), cancellationToken)
                    : await WaitForConsoleTextAsync(
                        classCfg, heavyCheckOffset, TimeSpan.FromMilliseconds(2500), cancellationToken);

                if (heavyVerified)
                    break;

                if (attempt < 4)
                {
                    // Fallback between the two common keyboard ways to leave TF2's team menu.
                    // If the key lands on the class menu instead, the following class key selects the target.
                    var teamFallbackKey = Vk1;
                    var teamFallbackName = "1";
                    AppLogger.Info($"AUTO CLASS: {className} not verified after attempt {attempt}; retrying Random team with key {teamFallbackName} before class key.");
                    if (!await SendAutoClassKeyAsync(teamFallbackKey, $"TEAM {teamFallbackName} retry {attempt}", cancellationToken))
                        return AutoClassKeyDeliveryFailed($"TEAM {teamFallbackName} retry {attempt}");
                    await Task.Delay(1200, cancellationToken);
                }
            }

            if (heavyVerified)
            {
                _joinConfirmed = true;
                Interlocked.Exchange(ref _joinStallReported, 0);
                AppLogger.Info($"AUTO CLASS verified: {className} selected (class CFG marker detected).");
                AutoHeavyStatusChanged?.Invoke($"Auto Class: {className} selected ✓");

                if (settings.MinimizeTf2AfterLaunch)
                {
                    // TF2 can retain the FPS mouse capture after the Windows window is
                    // minimized. ESC opens TF2's in-game menu before minimizing and
                    // releases the cursor without moving the player's aim.
                    // Do this on every verified join, including map-change recovery.
                    // Never send ESC to an unrelated foreground window.
                    await Task.Delay(550, cancellationToken);
                    if (!await SendAutoClassKeyAsync(VkEscape, "ESC before minimize", cancellationToken))
                    {
                        AppLogger.Warning("AUTO CLASS: ESC was not delivered to TF2. Leaving the game visible rather than minimizing with the mouse captured.");
                        AutoHeavyStatusChanged?.Invoke($"Auto Class: {className} selected; ESC failed, TF2 left visible");
                        return true;
                    }

                    AppLogger.Info("AUTO CLASS: ESC delivered to TF2 after verified class selection; preparing to minimize.");
                    await Task.Delay(350, cancellationToken);
                    if (!MinimizeTf2())
                        AppLogger.Warning("AUTO CLASS: TF2 was verified but could not be minimized; window left visible.");
                }

                return true;
            }

            AppLogger.Warning($"AUTO CLASS: {className} selection was not verified. TF2 is intentionally left visible for inspection.");
            AutoHeavyStatusChanged?.Invoke($"Auto Class: {className} not verified");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AutoHeavyStatusChanged?.Invoke("Auto Class: cancelled");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Auto Class worker failed.", ex);
            AutoHeavyStatusChanged?.Invoke("Auto Class: error");
            return false;
        }
        finally
        {
            // Discard all console text from the just-finished join sequence and arm the
            // in-process transition detector only after the initial/recovery sequence has run.
            // This prevents the initial server_spawn from immediately retriggering Auto Class.
            if (_joinConfirmed && readySeen && IsTf2Running)
            {
                _healthConsoleOffset = GetConsoleLogLength();
                _serverTransitionWatchArmed = true;
                AppLogger.Info("SERVER TRANSITION WATCH armed after Auto Class join sequence.");
            }
        }
    }

    private bool AutoClassKeyDeliveryFailed(string label)
    {
        AppLogger.Warning($"AUTO CLASS: aborted at {label}. Windows did not confirm TF2 focus or key delivery. Remaining steps were NOT sent.");
        AutoHeavyStatusChanged?.Invoke($"Auto Class: input failed at {label}");
        return false;
    }

    private async Task<bool> SendAutoClassKeyAsync(ushort key, string label, CancellationToken cancellationToken)
    {
        // Another app, notification, or Steam overlay can steal focus between
        // menu steps. Re-check it before *every* SendInput, not only once at start.
        if (!await FocusTf2ForInputAsync(TimeSpan.FromSeconds(8), cancellationToken))
        {
            AppLogger.Warning($"AUTO CLASS: no TF2 foreground confirmation before {label}; refusing to type into another app.");
            return false;
        }

        var process = FindTf2Process();
        if (process is null)
            return false;
        var target = GetTf2InputWindow(process);
        return target != IntPtr.Zero && PressGameKey(target, key, label);
    }

    private async Task<bool> FocusTf2ForInputAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var attempt = 0;
        var lastReport = DateTimeOffset.MinValue;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = FindTf2Process();
            if (process is null)
            {
                AppLogger.Warning("AUTO CLASS FOCUS: TF2 process disappeared while waiting to send keys.");
                return false;
            }

            try
            {
                var handle = GetTf2InputWindow(process);
                var foreground = GetForegroundWindow();
                if (handle != IntPtr.Zero && IsTf2Foreground(process.Id))
                    return true;

                if (handle != IntPtr.Zero)
                {
                    // Normal activation first. GetForegroundWindow is verified below.
                    ShowWindowAsync(handle, SwRestore);
                    BringWindowToTop(handle);
                    SetForegroundWindow(handle);
                    if (!IsTf2Foreground(process.Id))
                    {
                        // Source/Direct3D windows are often loaded on another thread.
                        // Attach our worker's input queue to the current foreground and
                        // TF2 window threads briefly while requesting activation.
                        TryActivateTf2WithAttachedInput(handle);
                    }
                }

                await Task.Delay(250, cancellationToken);
                if (IsTf2Foreground(process.Id))
                {
                    AppLogger.Info($"AUTO CLASS FOCUS OK: targetPid={process.Id} | window=0x{handle.ToInt64():X} | attempt={attempt + 1}.");
                    return true;
                }

                if (DateTimeOffset.UtcNow - lastReport > TimeSpan.FromSeconds(3))
                {
                    var current = GetForegroundWindow();
                    GetWindowThreadProcessId(current, out var foregroundPid);
                    var win32 = Marshal.GetLastWin32Error();
                    AppLogger.Warning($"AUTO CLASS FOCUS RETRY: targetPid={process.Id} | window=0x{handle.ToInt64():X} | foregroundPid={foregroundPid} | foregroundWindow=0x{current.ToInt64():X} | Win32={win32}. No game keys sent yet.");
                    lastReport = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"AUTO CLASS FOCUS retry error: {ex.GetType().Name}: {ex.Message}");
            }

            attempt++;
            await Task.Delay(350, cancellationToken);
        }

        return false;
    }

    private static bool IsTf2Foreground(int expectedPid)
    {
        var current = GetForegroundWindow();
        if (current == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(current, out var actualPid);
        return actualPid == (uint)expectedPid;
    }

    private static IntPtr GetTf2InputWindow(Process process)
    {
        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle != IntPtr.Zero && IsWindow(handle))
            return handle;

        // Some Source startup/reconnect sequences create their Direct3D top-level
        // window later than Process.MainWindowHandle becomes available.
        IntPtr result = IntPtr.Zero;
        EnumWindows((candidate, _) =>
        {
            GetWindowThreadProcessId(candidate, out var pid);
            if (pid == (uint)process.Id && IsWindowVisible(candidate) && GetWindow(candidate, GwOwner) == IntPtr.Zero)
            {
                result = candidate;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static void TryActivateTf2WithAttachedInput(IntPtr target)
    {
        var ownThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(target, out _);
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        if (targetThread == 0)
            return;

        // PeekMessage creates a message queue for this background worker if it
        // does not have one. AttachThreadInput requires both queues to exist.
        PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        var toForeground = foregroundThread != 0 && foregroundThread != ownThread &&
                           AttachThreadInput(ownThread, foregroundThread, true);
        var toTarget = targetThread != ownThread && targetThread != foregroundThread &&
                       AttachThreadInput(ownThread, targetThread, true);
        try
        {
            BringWindowToTop(target);
            SetForegroundWindow(target);
        }
        finally
        {
            if (toTarget) AttachThreadInput(ownThread, targetThread, false);
            if (toForeground) AttachThreadInput(ownThread, foregroundThread, false);
        }
    }

    private static bool PressGameKey(IntPtr targetWindow, ushort virtualKey, string label)
    {
        // v0.3.4 defined INPUT's union with KEYBDINPUT only. On x64 that made
        // Marshal.SizeOf<INPUT>() 32 bytes while Win32 expects the real INPUT
        // structure to be 40 bytes (MOUSEINPUT is the largest union member).
        // SendInput therefore returned 0/ERROR_INVALID_PARAMETER before TF2 saw a key.
        var scanCode = (ushort)MapVirtualKey(virtualKey, MapVkVkToVsc);
        var inputSize = Marshal.SizeOf<INPUT>();

        var inputs = new[]
        {
            new INPUT
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = 0,
                        ScanCode = scanCode,
                        Flags = KeyEventScancode,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            },
            new INPUT
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = 0,
                        ScanCode = scanCode,
                        Flags = KeyEventScancode | KeyEventKeyUp,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            }
        };

        SetLastError(0);
        var sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent == (uint)inputs.Length)
        {
            AppLogger.Info($"AUTO CLASS INPUT OK: {label} | vk=0x{virtualKey:X2} | scan=0x{scanCode:X2} | INPUT={inputSize} bytes | SendInput={sent}/2");
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        AppLogger.Warning($"AUTO CLASS SendInput failed: {label} | vk=0x{virtualKey:X2} | scan=0x{scanCode:X2} | INPUT={inputSize} bytes | SendInput={sent}/2 | Win32={error}. Trying WM_KEYDOWN/WM_KEYUP fallback.");

        if (targetWindow == IntPtr.Zero)
            return false;

        var lParamDown = BuildKeyLParam(scanCode, keyUp: false);
        var lParamUp = BuildKeyLParam(scanCode, keyUp: true);
        var downOk = PostMessage(targetWindow, WmKeyDown, (UIntPtr)virtualKey, lParamDown);
        Thread.Sleep(70);
        var upOk = PostMessage(targetWindow, WmKeyUp, (UIntPtr)virtualKey, lParamUp);

        if (downOk && upOk)
        {
            AppLogger.Info($"AUTO CLASS INPUT FALLBACK OK: {label} via WM_KEYDOWN/WM_KEYUP.");
            return true;
        }

        AppLogger.Warning($"AUTO CLASS window-message fallback failed: {label} | down={downOk} | up={upOk} | Win32={Marshal.GetLastWin32Error()}");
        return false;
    }

    private static IntPtr BuildKeyLParam(ushort scanCode, bool keyUp)
    {
        long value = 1L | ((long)scanCode << 16);
        if (keyUp)
            value |= 1L << 30 | 1L << 31;
        return new IntPtr(value);
    }

    private async Task<bool> WaitForLocalPlayerReadyAsync(
        string playerName,
        long startOffset,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            var markers = new[]
            {
                $"{playerName} se ha conectado",
                $"{playerName} connected"
            };

            if (await WaitForAnyConsoleTextAsync(markers, startOffset, timeout, cancellationToken))
                return true;
        }

        // Fallback: some custom configs/locales alter the connection message.
        // The MOTD-blocking line occurs much later than server_spawn and is safer as a readiness hint.
        return await WaitForAnyConsoleTextAsync(
            new[] { "Blocking HTML info panel", "Using plaintext instead" },
            startOffset,
            TimeSpan.FromSeconds(20),
            cancellationToken
        );
    }

    private async Task<bool> WaitForAnyConsoleTextAsync(
        IEnumerable<string> needles,
        long startOffset,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_detection.Tf2ConsoleLog))
            return false;

        var wanted = needles.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (wanted.Length == 0)
            return false;

        var path = _detection.Tf2ConsoleLog!;
        var deadline = DateTimeOffset.UtcNow + timeout;
        var position = Math.Max(0, startOffset);
        var carry = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(path))
                {
                    var length = new FileInfo(path).Length;
                    if (length < position)
                    {
                        position = 0;
                        carry = string.Empty;
                    }

                    if (length > position)
                    {
                        await using var stream = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete
                        );
                        stream.Seek(position, SeekOrigin.Begin);
                        var remaining = checked((int)Math.Min(length - position, 1024 * 1024));
                        var buffer = new byte[remaining];
                        var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), cancellationToken);
                        position += read;

                        var chunk = carry + Encoding.UTF8.GetString(buffer, 0, read);
                        if (wanted.Any(needle => chunk.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                            return true;

                        carry = chunk.Length > 1024 ? chunk[^1024..] : chunk;
                    }
                }
            }
            catch (IOException)
            {
                // console.log is actively written by TF2; retry on transient sharing/read races.
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private Task<bool> WaitForConsoleTextAsync(
        string needle,
        long startOffset,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => WaitForAnyConsoleTextAsync(new[] { needle }, startOffset, timeout, cancellationToken);

    private long GetConsoleLogLength()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_detection.Tf2ConsoleLog) && File.Exists(_detection.Tf2ConsoleLog))
                return new FileInfo(_detection.Tf2ConsoleLog).Length;
        }
        catch { }
        return 0;
    }

    private void CancelAutoHeavyWorker()
    {
        var cts = _autoHeavyCts;
        _autoHeavyCts = null;
        _autoHeavyTask = null;
        if (cts is null)
            return;

        try { cts.Cancel(); } catch { }
        cts.Dispose();
    }

    private Process? FindTf2Process()
    {
        if (_trackedProcess is not null)
        {
            try
            {
                if (!_trackedProcess.HasExited && IsProcessInsideTf2(_trackedProcess))
                    return _trackedProcess;
            }
            catch { }
        }

        foreach (var name in new[] { "tf_win64", "tf", "hl2" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (IsProcessInsideTf2(process))
                    {
                        _trackedProcess = process;
                        return process;
                    }
                }
                catch
                {
                    process.Dispose();
                }
            }
        }

        return null;
    }

    private bool IsProcessInsideTf2(Process process)
    {
        if (string.IsNullOrWhiteSpace(_detection.Tf2Directory))
            return true;

        try
        {
            var fileName = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var tf2 = Path.GetFullPath(_detection.Tf2Directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var exe = Path.GetFullPath(fileName);
            return exe.StartsWith(tf2, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return process.ProcessName.Equals("tf_win64", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("tf", StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<Process?> WaitForTf2ProcessAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var end = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = FindTf2Process();
            if (process is not null)
                return process;
            await Task.Delay(750, cancellationToken);
        }
        return null;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private string ReadNewConsoleText(ref long position)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_detection.Tf2ConsoleLog) || !File.Exists(_detection.Tf2ConsoleLog))
                return string.Empty;

            var path = _detection.Tf2ConsoleLog!;
            var length = new FileInfo(path).Length;
            if (length < position)
                position = 0;
            if (length <= position)
                return string.Empty;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(position, SeekOrigin.Begin);
            var remaining = checked((int)Math.Min(length - position, 512 * 1024));
            var buffer = new byte[remaining];
            var read = stream.Read(buffer, 0, remaining);
            position += read;
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Watchdog could not read console.log: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task ForceCloseTf2Async()
    {
        var process = FindTf2Process();
        if (process is null)
        {
            StartedAt = null;
            LastRequestedServer = null;
            _trackedProcess = null;
            return;
        }

        try
        {
            if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
            {
                process.CloseMainWindow();
                var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(4));
                if (!exited && !process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            else if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Watchdog graceful TF2 restart close failed: {ex.Message}");
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx)
            {
                AppLogger.Error("Watchdog could not terminate TF2 for recovery.", killEx);
                throw;
            }
        }
        finally
        {
            await Task.Delay(400);
            StartedAt = null;
            LastRequestedServer = null;
            _trackedProcess = null;
        }
    }

    private void CleanupLegacyDropSentinel()
    {
        if (string.IsNullOrWhiteSpace(_detection.Tf2Directory))
            return;

        try
        {
            var legacyRoot = Path.Combine(
                _detection.Tf2Directory,
                "tf",
                "custom",
                "tf2_idle_manager_drop_sentinel"
            );

            if (Directory.Exists(legacyRoot))
            {
                Directory.Delete(legacyRoot, recursive: true);
                AppLogger.Info($"Removed legacy v0.2.1 drop sentinel: {legacyRoot}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not remove legacy v0.2.1 drop sentinel: {ex.Message}");
        }
    }

    private void WriteIdleCfg(string cfgDirectory, AppSettings settings)
    {
        var path = Path.Combine(cfgDirectory, IdleCfgName);
        var lines = new List<string>
        {
            "// Generated by TF2 Idle Manager v1.0.0",
            "echo \"[TF2 Idle Manager] idle cfg loaded\"",
            "con_timestamp 1",
            "con_enable 1",
            "-attack",
            "-left"
        };

        if (settings.AutoSelectHeavy)
        {
            lines.Add("cl_disablehtmlmotd 1");
            lines.Add("echo \"[TF2 Idle Manager] Auto Class armed; manager waits for local player connection then uses Enter x2 -> 1 -> selected class\"");
        }

        // Arm idle movement immediately. It becomes meaningful once a class is spawned.
        if (settings.AttackEnabled)
            lines.Add("+attack");
        if (settings.LeftEnabled)
            lines.Add("+left");

        lines.Add("echo \"[TF2 Idle Manager] idle commands applied\"");

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AppLogger.Info($"Idle CFG written: {path} | autoClass={settings.EffectiveClass}");
    }

    public static string? NormalizeServerForSettings(string? input) => NormalizeServer(input);

    private static string? NormalizeServer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if (value.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
            value = value[8..].Trim();
        if (value.StartsWith("steam://connect/", StringComparison.OrdinalIgnoreCase))
            value = value["steam://connect/".Length..].Trim().TrimEnd('/');

        return ServerRegex().IsMatch(value) ? value : null;
    }

    private static IEnumerable<string> SplitLaunchOptions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        foreach (Match match in LaunchOptionRegex().Matches(raw))
        {
            var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+:\d{1,5}$")]
    private static partial Regex ServerRegex();

    [GeneratedRegex("\\\"([^\\\"]*)\\\"|(\\S+)")]
    private static partial Regex LaunchOptionRegex();

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScancode = 0x0008;
    private const uint MapVkVkToVsc = 0;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const ushort VkReturn = 0x0D;
    private const ushort VkEscape = 0x1B;
    private const ushort Vk1 = 0x31;
    private const ushort Vk5 = 0x35;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;

        [FieldOffset(0)]
        public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }

    private const int SwRestore = 9;
    private const uint PmNoRemove = 0;
    private const uint GwOwner = 4;

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr hWnd, uint min, uint max, uint remove);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint dwErrCode);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
