namespace VintageModUpdater.Core;

internal static class ModDbTrustPolicy
{
    public const string ModDbHost = "mods.vintagestory.at";

    public const string ModDbCdnHost = "moddbcdn.vintagestory.at";

    public static bool IsTrustedApiHost(Uri uri)
    {
        return IsHttps(uri)
            && uri.Host.Equals(ModDbHost, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTrustedDownloadEntryUri(Uri uri)
    {
        return IsTrustedApiHost(uri);
    }

    public static bool IsTrustedDownloadFinalUri(Uri uri)
    {
        return IsHttps(uri)
            && (uri.Host.Equals(ModDbHost, StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals(ModDbCdnHost, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHttps(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
