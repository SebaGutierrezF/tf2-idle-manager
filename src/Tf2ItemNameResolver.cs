using System.Net.Http.Headers;
using System.Text.Json;

namespace TF2IdleManager;

public sealed class Tf2ItemNameResolver : IDisposable
{
    private sealed class NameCacheFile
    {
        public int Version { get; set; } = 4;
        public DateTimeOffset UpdatedAt { get; set; }
        public int NextStart { get; set; }
        public bool SchemaExhausted { get; set; }
        public Dictionary<uint, string> Names { get; set; } = new();
        public Dictionary<uint, string> ImageUrls { get; set; } = new();
        public Dictionary<uint, string> ItemClasses { get; set; } = new();
        public Dictionary<uint, string> CraftClasses { get; set; } = new();
    }

    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<uint, string> _names = new();
    private readonly Dictionary<uint, string> _imageUrls = new();
    private readonly Dictionary<uint, string> _itemClasses = new();
    private readonly Dictionary<uint, string> _craftClasses = new();
    private int _nextStart;
    private bool _schemaExhausted;
    private DateTimeOffset _cacheUpdatedAt;
    private DateTime _lastCacheWriteUtc = DateTime.MinValue;

    private static string CachePath => Path.Combine(AppLogger.AppDataDirectory, "tf2-item-names-es.json");

    public Tf2ItemNameResolver()
    {
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TF2IdleManager", "0.5.2"));
        LoadCache();
    }

    // Another resolver (the live drop monitor) can update the shared schema
    // cache. Refresh the dashboard copy only when the file actually changes.
    public void ReloadFromDiskIfChanged()
    {
        try
        {
            if (File.Exists(CachePath) && File.GetLastWriteTimeUtc(CachePath) > _lastCacheWriteUtc)
                LoadCache();
        }
        catch (IOException) { /* Noncritical artwork data: retry later. */ }
    }

    public string? GetCachedImageUrl(uint defindex) => _imageUrls.TryGetValue(defindex, out var url) ? SteamItemImageUrl.Normalize(url) : null;

    public async Task ResolveAsync(string apiKey, IList<PendingDropItem> items, CancellationToken cancellationToken, int maxPages = 8)
    {
        var unresolved = items
            .Select(x => x.DefIndex)
            .Where(x => !_names.ContainsKey(x) || !_itemClasses.ContainsKey(x) || !_imageUrls.ContainsKey(x))
            .Distinct()
            .ToHashSet();

        if (unresolved.Count == 0)
        {
            ApplyKnownNames(items);
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            unresolved.RemoveWhere(x => _names.ContainsKey(x) && _itemClasses.ContainsKey(x) && _imageUrls.ContainsKey(x));

            // A fully exhausted schema cache is useful, but TF2 adds new items over time.
            // Re-open the scan after three days if a newly seen defindex is still unknown.
            if (_schemaExhausted && DateTimeOffset.UtcNow - _cacheUpdatedAt > TimeSpan.FromDays(3))
            {
                _schemaExhausted = false;
                _nextStart = 0;
            }

            var pages = 0;
            var changed = false;

            while (unresolved.Count > 0 && !_schemaExhausted && pages < Math.Clamp(maxPages, 1, 8))
            {
                pages++;
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.steampowered.com/IEconItems_440/GetSchemaItems/v0001/?key={Uri.EscapeDataString(apiKey.Trim())}&format=json&language=spanish&start={_nextStart}"
                );
                request.Headers.TryAddWithoutValidation("x-webapi-key", apiKey.Trim());

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warning($"TF2 item-name schema lookup returned HTTP {(int)response.StatusCode}; using cached/defindex fallback names.");
                    break;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("result", out var result))
                    break;

                if (result.TryGetProperty("items", out var schemaItems) && schemaItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var schemaItem in schemaItems.EnumerateArray())
                    {
                        if (!schemaItem.TryGetProperty("defindex", out var defNode) || !defNode.TryGetUInt32(out var defIndex))
                            continue;

                        var name = schemaItem.TryGetProperty("item_name", out var nameNode)
                            ? nameNode.GetString()
                            : null;

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            if (!_names.TryGetValue(defIndex, out var existing) || !string.Equals(existing, name, StringComparison.Ordinal))
                            {
                                _names[defIndex] = name!;
                                changed = true;
                            }
                        }

                        // Large artwork is preferred for the dashboard, but many older
                        // schema rows provide only image_url. The schema often emits
                        // http://media.steampowered.com; upgrade trusted URLs to HTTPS.
                        string? imageUrl = null;
                        foreach (var imageField in new[] { "image_url_large", "image_url" })
                        {
                            if (schemaItem.TryGetProperty(imageField, out var imageNode) &&
                                imageNode.ValueKind == JsonValueKind.String)
                            {
                                imageUrl = SteamItemImageUrl.Normalize(imageNode.GetString());
                                if (imageUrl is not null) break;
                            }
                        }
                        if (imageUrl is not null &&
                            (!_imageUrls.TryGetValue(defIndex, out var oldUrl) || oldUrl != imageUrl))
                        {
                            _imageUrls[defIndex] = imageUrl;
                            changed = true;
                        }

                        var itemClass = schemaItem.TryGetProperty("item_class", out var itemClassNode)
                            ? itemClassNode.GetString()
                            : null;
                        if (!string.IsNullOrWhiteSpace(itemClass) &&
                            (!_itemClasses.TryGetValue(defIndex, out var existingItemClass) || !string.Equals(existingItemClass, itemClass, StringComparison.Ordinal)))
                        {
                            _itemClasses[defIndex] = itemClass!;
                            changed = true;
                        }

                        var craftClass = schemaItem.TryGetProperty("craft_class", out var craftClassNode)
                            ? craftClassNode.GetString()
                            : null;
                        if (!string.IsNullOrWhiteSpace(craftClass) &&
                            (!_craftClasses.TryGetValue(defIndex, out var existingCraftClass) || !string.Equals(existingCraftClass, craftClass, StringComparison.Ordinal)))
                        {
                            _craftClasses[defIndex] = craftClass!;
                            changed = true;
                        }
                    }
                }

                unresolved.RemoveWhere(x => _names.ContainsKey(x) && _itemClasses.ContainsKey(x) && _imageUrls.ContainsKey(x));

                if (!result.TryGetProperty("next", out var nextNode) || !nextNode.TryGetInt32(out var next) || next <= _nextStart)
                {
                    _schemaExhausted = true;
                    changed = true;
                    break;
                }

                _nextStart = next;
                changed = true;
            }

            if (changed)
            {
                _cacheUpdatedAt = DateTimeOffset.UtcNow;
                SaveCache();
                AppLogger.Info($"TF2 schema progress: pages={pages}, names={_names.Count}, images={_imageUrls.Count}, next={_nextStart}, exhausted={_schemaExhausted}");
            }

            ApplyKnownNames(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"TF2 item-name lookup failed; using cached/defindex fallback names: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    // Opportunistic, budgeted schema hydration for existing historical drops.
    // Uses one or two schema pages, never a request per item, and does not
    // touch GetPlayerItems or affect the notification/watchdog pipeline.
    public async Task<int> WarmHistoricalImagesAsync(
        string apiKey, IReadOnlyCollection<uint> defindices, CancellationToken cancellationToken)
    {
        var missing = defindices.Distinct().Where(x => GetCachedImageUrl(x) is null).ToArray();
        if (missing.Length == 0 || string.IsNullOrWhiteSpace(apiKey)) return 0;
        var items = missing.Select(x => new PendingDropItem(0, x, 0, 0, null)).ToList();
        await ResolveAsync(apiKey, items, cancellationToken, maxPages: 2);
        return missing.Count(x => GetCachedImageUrl(x) is not null);
    }

    private void ApplyKnownNames(IList<PendingDropItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            _names.TryGetValue(item.DefIndex, out var name);
            _itemClasses.TryGetValue(item.DefIndex, out var itemClass);
            _craftClasses.TryGetValue(item.DefIndex, out var craftClass);
            items[i] = item with
            {
                ResolvedName = string.IsNullOrWhiteSpace(name) ? item.ResolvedName : name,
                ImageUrl = GetCachedImageUrl(item.DefIndex),
                ItemClass = itemClass,
                CraftClass = craftClass
            };
        }
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return;

            var json = File.ReadAllText(CachePath);
            var cache = JsonSerializer.Deserialize<NameCacheFile>(json);
            if (cache is null)
                return;

            foreach (var pair in cache.ImageUrls ?? new Dictionary<uint, string>())
            {
                var normalized = SteamItemImageUrl.Normalize(pair.Value);
                if (normalized is not null) _imageUrls[pair.Key] = normalized;
            }
            foreach (var pair in cache.Names ?? new Dictionary<uint, string>())
                _names[pair.Key] = pair.Value;
            foreach (var pair in cache.ItemClasses ?? new Dictionary<uint, string>())
                _itemClasses[pair.Key] = pair.Value;
            foreach (var pair in cache.CraftClasses ?? new Dictionary<uint, string>())
                _craftClasses[pair.Key] = pair.Value;

            _nextStart = Math.Max(0, cache.NextStart);
            _schemaExhausted = cache.SchemaExhausted;
            _cacheUpdatedAt = cache.UpdatedAt;
            _lastCacheWriteUtc = File.GetLastWriteTimeUtc(CachePath);

            // v0.4.3 and older caches contained names only. Re-scan once so container/case
            // classification can use schema item_class/craft_class instead of guessing by name.
            if (cache.Version < 4 || _itemClasses.Count == 0)
            {
                _nextStart = 0;
                _schemaExhausted = false;
            }

            AppLogger.Info($"TF2 item metadata cache loaded | names={_names.Count} | itemClasses={_itemClasses.Count} | next={_nextStart} | exhausted={_schemaExhausted}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not load TF2 item-name cache: {ex.Message}");
        }
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(AppLogger.AppDataDirectory);
            var cache = new NameCacheFile
            {
                Version = 4,
                UpdatedAt = _cacheUpdatedAt == default ? DateTimeOffset.UtcNow : _cacheUpdatedAt,
                NextStart = _nextStart,
                SchemaExhausted = _schemaExhausted,
                Names = new Dictionary<uint, string>(_names),
                ImageUrls = new Dictionary<uint, string>(_imageUrls),
                ItemClasses = new Dictionary<uint, string>(_itemClasses),
                CraftClasses = new Dictionary<uint, string>(_craftClasses)
            };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            _lastCacheWriteUtc = File.GetLastWriteTimeUtc(CachePath);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not save TF2 item-name cache: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        _http.Dispose();
    }
}
