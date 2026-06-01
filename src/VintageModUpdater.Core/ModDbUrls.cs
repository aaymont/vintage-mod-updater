namespace VintageModUpdater.Core;

public static class ModDbUrls
{
    public static readonly Uri BaseUri = new("https://mods.vintagestory.at/");

    public static string? GetModPageUrl(int? assetId)
    {
        if (assetId is not > 0)
        {
            return null;
        }

        var pageUri = new Uri(BaseUri, $"show/mod/{assetId.Value}");
        return ModDbTrustPolicy.IsTrustedModPageUri(pageUri) ? pageUri.ToString() : null;
    }

    public static bool IsAllowedBrowserUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && ModDbTrustPolicy.IsTrustedModPageUri(uri);
    }
}
