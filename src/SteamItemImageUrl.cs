namespace TF2IdleManager;

// Valve's older TF2 schema still emits HTTP image_url fields. Convert those
// *trusted Steam CDN* links to HTTPS; never fetch arbitrary third-party URLs.
internal static class SteamItemImageUrl
{
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) ||
            !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || (!uri.IsDefaultPort && uri.Port != 443 && uri.Port != 80))
            return null;

        var host = uri.Host;
        var approved = host.Equals("media.steampowered.com", StringComparison.OrdinalIgnoreCase) ||
                       host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                       host.Equals("steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
                       host.Equals("steamusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".steamusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                       host.Equals("steampowered.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".steampowered.com", StringComparison.OrdinalIgnoreCase) ||
                       (host.StartsWith("steamcommunity-", StringComparison.OrdinalIgnoreCase) &&
                        host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase));
        if (!approved) return null;
        // Always fetch artwork over TLS, including old GetSchemaItems HTTP URLs.
        var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
        return builder.Uri.AbsoluteUri;
    }
}
