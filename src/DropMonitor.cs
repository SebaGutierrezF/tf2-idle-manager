using System.Net.Http.Headers;
using System.Text.Json;

namespace TF2IdleManager;

public sealed class DropMonitor : IDisposable
{
    private const uint BackendPositionUnacked = 1u << 30;
    private const uint BackendPositionNewFormat = 1u << 31;
    private const uint BackendPositionFormatMask = BackendPositionUnacked | BackendPositionNewFormat;
    private const uint UnackReasonMask = 0x0000ffff;
    private const uint UnackReasonDropped = 1;

    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Tf2ItemNameResolver _nameResolver = new();
    private readonly Dictionary<ulong, PendingDropItem> _previousPending = new();
    private readonly HashSet<ulong> _seenIds = new();

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private string? _apiKey;
    private string? _secondaryApiKey;
    private bool _usingSecondary;
    private string? _steamId64;
    private bool _baselineReady;
    private bool _isDegraded;
    private int _consecutiveFailures;
    private DateTimeOffset? _outageStartedAt;

    public bool IsRunning => _worker is { IsCompleted: false };
    public bool BaselineReady => _baselineReady;
    public bool IsDegraded => _isDegraded;
    public int ConsecutiveFailures => _consecutiveFailures;
    public DateTimeOffset? OutageStartedAt => _outageStartedAt;
    public DateTimeOffset? LastSuccessfulCheck { get; private set; }
    public int PendingCount { get; private set; }

    public event Action<DropDetectedEvent>? DropDetected;
    public event Action<DropAcknowledgedEvent>? DropAcknowledged;
    public event Action<DropSnapshot>? SnapshotChanged;
    public event Action<string>? StatusChanged;
    public event Action<DropMonitorAvailabilityEvent>? AvailabilityChanged;
    public event Action<DropSnapshot>? BaselineRecoveredWithPending;
    public event Action<DropBaselineEvent>? BaselineEstablished;

    public DropMonitor()
    {
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TF2IdleManager", "0.5.0.1"));
    }

    public async Task<int> StartAsync(string apiKey, string steamId64, CancellationToken cancellationToken = default)
    {
        Stop(clearIdentity: false);

        _apiKey = NormalizeApiKey(apiKey);
        _secondaryApiKey = ApiKeyStore.LoadSecondary();
        _usingSecondary = false;
        _steamId64 = NormalizeSteamId64(steamId64);
        _previousPending.Clear();
        _seenIds.Clear();
        LastSuccessfulCheck = null;
        PendingCount = 0;
        _baselineReady = false;
        _isDegraded = false;
        _consecutiveFailures = 0;
        _outageStartedAt = null;

        StatusChanged?.Invoke("Drop monitor: building baseline...");
        var baselineCount = -1;
        try
        {
            var baseline = await FetchSnapshotAsync(cancellationToken);
            InitializeBaseline(baseline, recoveredAfterApiOutage: false);
            baselineCount = baseline.PendingCount;
            MarkAvailable("Steam TF2 inventory API baseline ready.");
        }
        catch (Exception ex) when (IsTransientApiError(ex))
        {
            AppLogger.Warning($"Steam API baseline unavailable; monitor will retry in background: {ex.Message}");
            MarkDegraded(1, $"Steam TF2 inventory API unavailable while building baseline: {ex.Message}");
            StatusChanged?.Invoke("⚠ Drop monitor: Steam API unavailable — alerts unavailable until recovery");
        }

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => MonitorLoopAsync(_cts.Token));

        AppLogger.Info($"Drop monitor started | source=Steam Web API | steamid={_steamId64} | interval=30s | baselinePending={(baselineCount < 0 ? "pending" : baselineCount)}");
        if (_baselineReady)
        {
            StatusChanged?.Invoke(PendingCount > 0
                ? $"Drop monitor: baseline {PendingCount} pending | watching for NEW drops"
                : "Drop monitor: baseline clear | watching every 30s");
        }

        return baselineCount;
    }

    public void Stop() => Stop(clearIdentity: true);

    private void Stop(bool clearIdentity)
    {
        var cts = _cts;
        _cts = null;
        _worker = null;

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
            AppLogger.Info("Drop monitor stopped.");
        }

        _previousPending.Clear();
        _seenIds.Clear();
        PendingCount = 0;
        _baselineReady = false;
        _isDegraded = false;
        _consecutiveFailures = 0;
        _outageStartedAt = null;

        if (clearIdentity)
        {
            _apiKey = null;
            _steamId64 = null;
        }

        StatusChanged?.Invoke("Drop monitor: off");
    }

    public async Task<DropSnapshot> CheckNowAsync(string apiKey, string steamId64, CancellationToken cancellationToken = default)
    {
        var oldKey = _apiKey;
        var oldSteamId = _steamId64;
        try
        {
            _apiKey = NormalizeApiKey(apiKey);
            _secondaryApiKey = ApiKeyStore.LoadSecondary();
            _steamId64 = NormalizeSteamId64(steamId64);
            return await FetchSnapshotWithRetryAsync(cancellationToken);
        }
        finally
        {
            if (IsRunning)
            {
                _apiKey = oldKey;
                _steamId64 = oldSteamId;
            }
        }
    }

    public async Task<DropSnapshot> RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Drop monitor is not running.");

        try
        {
            var snapshot = await FetchSnapshotWithRetryAsync(cancellationToken);

            if (_isDegraded)
                MarkAvailable("Steam TF2 inventory API recovered during an immediate refresh.");

            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                if (!_baselineReady)
                    InitializeBaseline(snapshot, recoveredAfterApiOutage: true);
                else
                    ProcessSnapshot(snapshot);
            }
            finally
            {
                _stateLock.Release();
            }

            _consecutiveFailures = 0;
            return snapshot;
        }
        catch (Exception ex) when (IsTransientApiError(ex))
        {
            var failures = Math.Max(1, _consecutiveFailures + 1);
            MarkDegraded(failures, $"Steam TF2 inventory API unavailable during immediate refresh: {ex.Message}");
            throw;
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Keep checking at a steady cadence during 5xx outages. The TF2 inventory endpoint
                // can recover intermittently, and multi-minute exponential backoff made it too easy
                // to miss the recovery window.
                var delay = consecutiveFailures == 0
                    ? TimeSpan.FromSeconds(30)
                    : TimeSpan.FromSeconds(20);

                await Task.Delay(delay, cancellationToken);
                var snapshot = await FetchSnapshotWithRetryAsync(cancellationToken);

                if (_isDegraded)
                {
                    var failedPolls = Math.Max(consecutiveFailures, _consecutiveFailures);
                    AppLogger.Info($"Steam API drop monitor recovered after {failedPolls} failed poll(s).");
                    MarkAvailable($"Steam TF2 inventory API recovered after {failedPolls} failed poll(s).");
                }

                await _stateLock.WaitAsync(cancellationToken);
                try
                {
                    if (!_baselineReady)
                    {
                        InitializeBaseline(snapshot, recoveredAfterApiOutage: true);
                        AppLogger.Info($"Drop monitor baseline recovered after Steam API outage | pending={snapshot.PendingCount}");

                        if (snapshot.PendingCount > 0)
                            BaselineRecoveredWithPending?.Invoke(snapshot);

                        StatusChanged?.Invoke(snapshot.PendingCount > 0
                            ? $"Drop monitor: baseline {snapshot.PendingCount} pending | watching for NEW drops"
                            : "Drop monitor: baseline clear | watching every 30s");
                    }
                    else
                    {
                        ProcessSnapshot(snapshot);
                    }
                }
                finally
                {
                    _stateLock.Release();
                }

                consecutiveFailures = 0;
                _consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                MarkDegraded(consecutiveFailures, $"Steam TF2 inventory API unavailable: {ex.Message}");

                // Log the start of an outage, then periodic reminders without flooding Activity.
                if (consecutiveFailures == 1 || consecutiveFailures % 5 == 0)
                    AppLogger.Warning($"Steam API drop check unavailable ({consecutiveFailures} consecutive poll(s)): {ex.Message}");

                if (consecutiveFailures == 1)
                    StatusChanged?.Invoke("⚠ Drop monitor: Steam API unavailable — DROP ALERTS DEGRADED; state preserved");
            }
        }
    }

    private async Task<DropSnapshot> FetchSnapshotWithRetryAsync(CancellationToken cancellationToken)
    {
        Exception? last = null;
        var retryDelays = new[] { 0, 5, 15 };

        for (var attempt = 0; attempt < retryDelays.Length; attempt++)
        {
            if (retryDelays[attempt] > 0)
                await Task.Delay(TimeSpan.FromSeconds(retryDelays[attempt]), cancellationToken);

            try
            {
                return await FetchSnapshotAsync(cancellationToken);
            }
            catch (Exception ex) when (IsTransientApiError(ex) && attempt < retryDelays.Length - 1)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("Steam API request failed after retries.");
    }

    private static bool IsTransientApiError(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase)
            || message.Contains("HTTP 502", StringComparison.OrdinalIgnoreCase)
            || message.Contains("HTTP 503", StringComparison.OrdinalIgnoreCase)
            || message.Contains("HTTP 504", StringComparison.OrdinalIgnoreCase)
            || ex is HttpRequestException
            || ex is TaskCanceledException;
    }


    private void InitializeBaseline(DropSnapshot baseline, bool recoveredAfterApiOutage)
    {
        _previousPending.Clear();
        _seenIds.Clear();
        foreach (var item in baseline.PendingItems)
        {
            _previousPending[item.ItemId] = item;
            _seenIds.Add(item.ItemId);
        }

        PendingCount = baseline.PendingCount;
        LastSuccessfulCheck = baseline.CheckedAt;
        _baselineReady = true;
        SnapshotChanged?.Invoke(baseline);
        BaselineEstablished?.Invoke(new DropBaselineEvent(baseline, recoveredAfterApiOutage));
    }

    private void ProcessSnapshot(DropSnapshot snapshot)
    {
        var current = snapshot.PendingItems.ToDictionary(x => x.ItemId);
        var now = snapshot.CheckedAt;

        foreach (var oldItem in _previousPending.Values.ToArray())
        {
            if (current.ContainsKey(oldItem.ItemId))
                continue;

            var acknowledged = new DropAcknowledgedEvent(oldItem, now);
            AppLogger.Info($"DROP ACKNOWLEDGED | steamid={_steamId64} | itemId={oldItem.ItemId} | defindex={oldItem.DefIndex} | category={Tf2DropClassifier.GetDropType(oldItem.ItemClass, oldItem.CraftClass, oldItem.DisplayName)} | name={oldItem.DisplayName}");
            DropAcknowledged?.Invoke(acknowledged);
        }

        foreach (var item in current.Values)
        {
            if (_previousPending.ContainsKey(item.ItemId))
                continue;
            if (!_seenIds.Add(item.ItemId))
                continue;

            var detected = new DropDetectedEvent(item, now);
            AppLogger.Info(
                $"DROP DETECTED | source=Steam Web API | steamid={_steamId64} | itemId={item.ItemId} | defindex={item.DefIndex} | inventory={item.InventoryPosition} | origin={item.Origin} | itemClass={item.ItemClass ?? "?"} | craftClass={item.CraftClass ?? "?"} | category={Tf2DropClassifier.GetDropType(item.ItemClass, item.CraftClass, item.DisplayName)} | name={item.DisplayName}"
            );
            DropDetected?.Invoke(detected);
        }

        _previousPending.Clear();
        foreach (var pair in current)
            _previousPending[pair.Key] = pair.Value;

        PendingCount = snapshot.PendingCount;
        LastSuccessfulCheck = snapshot.CheckedAt;
        SnapshotChanged?.Invoke(snapshot);
        StatusChanged?.Invoke(PendingCount > 0
            ? $"Drop monitor: {PendingCount} pending | watching for NEW drops"
            : $"Drop monitor: 0 pending | checked {snapshot.CheckedAt:HH:mm:ss}");
    }

    private async Task<DropSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var apiKey = NormalizeApiKey(_apiKey);
        var steamId64 = NormalizeSteamId64(_steamId64);

        await _pollLock.WaitAsync(cancellationToken);
        try
        {
            using var response = await FetchInventoryResponseWithFallbackAsync(apiKey, steamId64, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Steam Web API HTTP {(int)response.StatusCode} ({response.ReasonPhrase})");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("result", out var result))
                throw new InvalidOperationException("Steam Web API response did not contain result.");

            var status = result.TryGetProperty("status", out var statusElement)
                ? statusElement.GetInt32()
                : 0;

            if (status != 1)
                throw new InvalidOperationException($"Steam TF2 inventory API returned status {status}.");

            var pending = new List<PendingDropItem>();
            if (result.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!TryReadUInt64(item, "id", out var id) ||
                        !TryReadUInt32(item, "defindex", out var defIndex) ||
                        !TryReadUInt32(item, "inventory", out var inventoryPosition))
                    {
                        continue;
                    }

                    var origin = item.TryGetProperty("origin", out var originElement) && originElement.TryGetInt32(out var parsedOrigin)
                        ? parsedOrigin
                        : -1;

                    if (!IsPendingDrop(inventoryPosition, origin))
                        continue;

                    pending.Add(new PendingDropItem(id, defIndex, inventoryPosition, origin, null));
                }
            }

            if (pending.Count > 0)
                await _nameResolver.ResolveAsync(apiKey, pending, cancellationToken);

            var checkedAt = DateTimeOffset.Now;
            LastSuccessfulCheck = checkedAt;
            return new DropSnapshot(pending, checkedAt);
        }
        finally
        {
            _pollLock.Release();
        }
    }

    // A secondary key is a credential-level fallback only. A shared Valve outage or
    // rate limit may make BOTH fail; do not report dual 503s as proof of a key problem.
    private async Task<HttpResponseMessage> FetchInventoryResponseWithFallbackAsync(
        string primaryKey, string steamId64, CancellationToken ct)
    {
        async Task<HttpResponseMessage> SendAsync(string key)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.steampowered.com/IEconItems_440/GetPlayerItems/v0001/?steamid={Uri.EscapeDataString(steamId64)}&format=json");
            req.Headers.TryAddWithoutValidation("x-webapi-key", key);
            req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            req.Headers.ConnectionClose = true;
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        var first = await SendAsync(primaryKey);
        if (first.IsSuccessStatusCode)
        {
            if (_usingSecondary) AppLogger.Info("Primary Steam API credential is responding again.");
            _usingSecondary = false;
            return first;
        }
        var code = (int)first.StatusCode;
        var secondary = _secondaryApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(secondary) || secondary == primaryKey ||
            (code != 401 && code != 403 && code != 429 && code < 500))
            return first;
        if (!_usingSecondary)
            AppLogger.Warning($"Primary Steam API credential received HTTP {code}; trying secondary credential.");
        try
        {
            var second = await SendAsync(secondary);
            first.Dispose();
            if (second.IsSuccessStatusCode)
            {
                if (!_usingSecondary) AppLogger.Info("Secondary Steam API credential succeeded for inventory checks.");
                _usingSecondary = true;
            }
            else if (!_usingSecondary)
                AppLogger.Warning($"Secondary credential also failed with HTTP {(int)second.StatusCode}. This may reflect a shared Steam service outage.");
            return second;
        }
        catch
        {
            first.Dispose();
            throw;
        }
    }

    private static bool IsPendingDrop(uint inventoryPosition, int origin)
    {
        var hasUnackedNewFormat = (inventoryPosition & BackendPositionFormatMask) == BackendPositionFormatMask;
        if (hasUnackedNewFormat)
        {
            var reason = inventoryPosition & UnackReasonMask;
            return reason == UnackReasonDropped;
        }

        return inventoryPosition == 0 && origin == 0;
    }

    private void MarkDegraded(int consecutiveFailures, string message)
    {
        var changed = !_isDegraded;
        _isDegraded = true;
        _consecutiveFailures = Math.Max(1, consecutiveFailures);
        _outageStartedAt ??= DateTimeOffset.Now;

        if (changed)
        {
            AvailabilityChanged?.Invoke(new DropMonitorAvailabilityEvent(
                false,
                _consecutiveFailures,
                DateTimeOffset.Now,
                _outageStartedAt,
                message
            ));
        }
    }

    private void MarkAvailable(string message)
    {
        var wasDegraded = _isDegraded;
        var priorOutageStart = _outageStartedAt;

        _isDegraded = false;
        _consecutiveFailures = 0;
        _outageStartedAt = null;

        if (wasDegraded)
        {
            AvailabilityChanged?.Invoke(new DropMonitorAvailabilityEvent(
                true,
                0,
                DateTimeOffset.Now,
                priorOutageStart,
                message
            ));
        }
    }

    private static string NormalizeApiKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length < 16)
            throw new InvalidOperationException("Enter a valid Steam Web API key first.");
        return key;
    }

    private static string NormalizeSteamId64(string? value)
    {
        var steamId = value?.Trim() ?? string.Empty;
        if (steamId.Length != 17 || !steamId.All(char.IsDigit))
            throw new InvalidOperationException("SteamID64 could not be detected from Steam loginusers.vdf.");
        return steamId;
    }

    private static bool TryReadUInt64(JsonElement element, string property, out ulong value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var node))
            return false;
        if (node.ValueKind == JsonValueKind.Number)
            return node.TryGetUInt64(out value);
        if (node.ValueKind == JsonValueKind.String)
            return ulong.TryParse(node.GetString(), out value);
        return false;
    }

    private static bool TryReadUInt32(JsonElement element, string property, out uint value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var node))
            return false;
        if (node.ValueKind == JsonValueKind.Number)
            return node.TryGetUInt32(out value);
        if (node.ValueKind == JsonValueKind.String)
            return uint.TryParse(node.GetString(), out value);
        return false;
    }

    private static string Shorten(string value, int max)
        => value.Length <= max ? value : value[..Math.Max(0, max - 1)] + "…";

    public void Dispose()
    {
        Stop();
        _nameResolver.Dispose();
        _pollLock.Dispose();
        _http.Dispose();
    }
}
