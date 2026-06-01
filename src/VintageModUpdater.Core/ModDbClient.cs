using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VintageModUpdater.Core;

public sealed partial class ModDbClient
{
    private static readonly Uri BaseUri = new("https://mods.vintagestory.at");
    private const int UpdaterModNumericId = 9231;
    private const int MaxApiResponseBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan ModDbRequestTimeout = TimeSpan.FromSeconds(60);
    private readonly HttpClient _httpClient;

    public ModDbClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout > ModDbRequestTimeout)
        {
            _httpClient.Timeout = ModDbRequestTimeout;
        }

        _httpClient.BaseAddress ??= BaseUri;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VintageModUpdater/0.1");
    }

    public async Task<IReadOnlyDictionary<string, ModUpdateStatus>> CheckUpdatesAsync(
        IEnumerable<InstalledMod> mods,
        string? gameVersion,
        CancellationToken cancellationToken = default)
    {
        var modList = mods
            .Where(mod => mod.CanCheckForUpdates)
            .GroupBy(mod => mod.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var statuses = new Dictionary<string, ModUpdateStatus>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            foreach (var mod in modList)
            {
                statuses[mod.Identifier] = new ModUpdateStatus(
                    mod.Identifier,
                    mod.Version,
                    ModUpdateKind.MissingGameVersion,
                    AvailableVersion: null,
                    DownloadFileName: null,
                    DownloadUrl: null,
                    ErrorCode: null,
                    "Set the Vintage Story install path so compatible updates can be checked.");
            }

            return statuses;
        }

        foreach (var chunk in modList.Chunk(40))
        {
            var specs = chunk.Select(mod => BuildModSpec(mod, includeVersion: true));
            var response = await RequestInstallInformationAsync(specs, gameVersion, cancellationToken).ConfigureAwait(false);

            foreach (var mod in chunk)
            {
                if (!response.TryGetValue(mod.Identifier, out var info))
                {
                    statuses[mod.Identifier] = ErrorStatus(mod, null, "ModDB did not return update information for this mod.");
                    continue;
                }

                statuses[mod.Identifier] = CreateStatus(mod, info);
            }
        }

        var updateCandidates = statuses
            .Values
            .Where(status => status.Kind == ModUpdateKind.UpdateAvailable && !string.IsNullOrWhiteSpace(status.AvailableVersion))
            .ToArray();

        foreach (var chunk in updateCandidates.Chunk(40))
        {
            var specs = chunk.Select(status => $"{status.ModId}@{status.AvailableVersion}");
            var response = await RequestInstallInformationAsync(specs, gameVersion, cancellationToken).ConfigureAwait(false);

            foreach (var candidate in chunk)
            {
                if (!response.TryGetValue(candidate.ModId, out var info))
                {
                    statuses[candidate.ModId] = candidate with
                    {
                        Message = "The compatible update was found, but its download details could not be resolved.",
                        DownloadFileName = null,
                        DownloadUrl = null
                    };
                    continue;
                }

                if (info.ErrorCode is not null)
                {
                    statuses[candidate.ModId] = candidate with
                    {
                        Message = info.Message ?? "The compatible update was found, but its download details could not be resolved.",
                        ErrorCode = info.ErrorCode,
                        DownloadFileName = null,
                        DownloadUrl = null
                    };
                    continue;
                }

                statuses[candidate.ModId] = candidate with
                {
                    DownloadFileName = info.FileName,
                    DownloadUrl = BuildDownloadUrl(info.FileUrl),
                    Message = $"Compatible update {candidate.AvailableVersion} is available."
                };
            }
        }

        return statuses;
    }

    public async Task<IReadOnlyList<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        var versions = await TryGetGameVersionsV2Async(cancellationToken).ConfigureAwait(false);
        if (versions.Count > 0)
        {
            return versions;
        }

        return await GetGameVersionsLegacyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int?> TryResolveAssetIdAsync(string modIdentifier, CancellationToken cancellationToken = default)
    {
        var reference = await TryResolveModReferenceCoreAsync(modIdentifier, cancellationToken).ConfigureAwait(false);
        return reference?.AssetId;
    }

    public Task<ModDbModReference?> TryResolveModReferenceAsync(
        string modIdentifier,
        CancellationToken cancellationToken = default)
    {
        return TryResolveModReferenceCoreAsync(modIdentifier, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, int>> ResolveModAssetIdsAsync(
        IEnumerable<string> modIdentifiers,
        CancellationToken cancellationToken = default)
    {
        var references = await ResolveModReferencesAsync(modIdentifiers, cancellationToken).ConfigureAwait(false);
        return references.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.AssetId,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, ModDbModReference>> ResolveModReferencesAsync(
        IEnumerable<string> modIdentifiers,
        CancellationToken cancellationToken = default)
    {
        var identifiers = modIdentifiers
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Select(identifier => identifier.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new Dictionary<string, ModDbModReference>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(6);
        var tasks = identifiers.Select(async identifier =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var reference = await TryResolveModReferenceCoreAsync(identifier, cancellationToken).ConfigureAwait(false);
                if (reference is not null)
                {
                    lock (results)
                    {
                        results[identifier] = reference;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    public async Task<AppUpdateStatus> CheckUpdaterAppUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var normalizedCurrentVersion = NormalizeVersion(currentVersion) ?? "0.0.0";
        using var response = await _httpClient
            .GetAsync($"/api/mod/{UpdaterModNumericId}", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !ModDbTrustPolicy.IsTrustedApiHost(finalUri))
        {
            throw new HttpRequestException("ModDB response originated from an unexpected host.");
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxApiResponseBytes)
        {
            throw new HttpRequestException("ModDB returned an unexpectedly large response body.");
        }

        var body = await ReadResponseBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ModDB returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(body, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (!document.RootElement.TryGetProperty("mod", out var modElement)
            || modElement.ValueKind != JsonValueKind.Object)
        {
            return new AppUpdateStatus(
                normalizedCurrentVersion,
                null,
                UpdateAvailable: false,
                ModDbUrls.BaseUri.ToString());
        }

        var latestVersion = ReadLatestReleaseVersion(modElement);
        var updateAvailable = !string.IsNullOrWhiteSpace(latestVersion)
            && VersionComparer.IsNewer(latestVersion, normalizedCurrentVersion);
        var modPageUrl = ModDbUrls.GetModPageUrl(ReadInt(modElement, "assetid"))
            ?? ModDbUrls.BaseUri.ToString();

        return new AppUpdateStatus(
            normalizedCurrentVersion,
            latestVersion,
            updateAvailable,
            modPageUrl);
    }

    private async Task<ModDbModReference?> TryResolveModReferenceCoreAsync(
        string modIdentifier,
        CancellationToken cancellationToken)
    {
        if (!IsValidModApiKey(modIdentifier))
        {
            return null;
        }

        using var response = await _httpClient
            .GetAsync($"/api/mod/{Uri.EscapeDataString(modIdentifier)}", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !ModDbTrustPolicy.IsTrustedApiHost(finalUri) || !response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxApiResponseBytes)
        {
            return null;
        }

        var body = await ReadResponseBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (!document.RootElement.TryGetProperty("mod", out var modElement)
            || modElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var assetId = ReadInt(modElement, "assetid");
        if (assetId is not > 0)
        {
            return null;
        }

        return new ModDbModReference(assetId.Value, ParseReleaseGameVersions(modElement));
    }

    private static IReadOnlyDictionary<string, string> ParseReleaseGameVersions(JsonElement modElement)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(modElement, "releases", out var releasesElement)
            || releasesElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var release in releasesElement.EnumerateArray())
        {
            var modVersion = ReadString(release, "modversion", "version");
            if (string.IsNullOrWhiteSpace(modVersion))
            {
                continue;
            }

            var formatted = GameVersionDisplay.FormatRange(ReadStringArray(release, "tags", "gameversions", "gameVersions"));
            if (formatted is not null)
            {
                results[modVersion.Trim()] = formatted;
            }
        }

        return results;
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }

            yield break;
        }
    }

    private static bool IsValidModApiKey(string modIdentifier)
    {
        return !string.IsNullOrWhiteSpace(modIdentifier)
            && ModApiKeyPattern().IsMatch(modIdentifier.Trim());
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex ModApiKeyPattern();

    private async Task<IReadOnlyList<string>> TryGetGameVersionsV2Async(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync("/api/v2/game-versions", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !ModDbTrustPolicy.IsTrustedApiHost(finalUri) || !response.IsSuccessStatusCode)
        {
            return Array.Empty<string>();
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxApiResponseBytes)
        {
            return Array.Empty<string>();
        }

        var body = await ReadResponseBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return ParseAndSortGameVersionNames(document.RootElement);
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && TryGetProperty(document.RootElement, "data", out var data)
            && data.ValueKind == JsonValueKind.Array)
        {
            return ParseAndSortGameVersionNames(data);
        }

        return Array.Empty<string>();
    }

    private async Task<IReadOnlyList<string>> GetGameVersionsLegacyAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync("/api/gameversions", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !ModDbTrustPolicy.IsTrustedApiHost(finalUri))
        {
            throw new HttpRequestException("ModDB response originated from an unexpected host.");
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxApiResponseBytes)
        {
            throw new HttpRequestException("ModDB returned an unexpectedly large response body.");
        }

        var body = await ReadResponseBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ModDB returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(body, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (!TryGetProperty(document.RootElement, "gameversions", out var gameVersions)
            || gameVersions.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var entry in gameVersions.EnumerateArray())
        {
            var name = ReadString(entry, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        return SortGameVersionNames(names);
    }

    private static IReadOnlyList<string> ParseAndSortGameVersionNames(JsonElement arrayElement)
    {
        var names = new List<string>();
        foreach (var entry in arrayElement.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var value = entry.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    names.Add(value.Trim());
                }

                continue;
            }

            var name = ReadString(entry, "name", "gameversion", "version");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        return SortGameVersionNames(names);
    }

    private static IReadOnlyList<string> SortGameVersionNames(IEnumerable<string> names)
    {
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => name, Comparer<string>.Create(VersionComparer.Compare))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, InstallInformation>> RequestInstallInformationAsync(
        IEnumerable<string> modSpecs,
        string gameVersion,
        CancellationToken cancellationToken)
    {
        var ids = string.Join(",", modSpecs);
        var requestUri = "/api/v2/mods/install-information"
            + $"?ids={WebUtility.UrlEncode(ids)}&gv={WebUtility.UrlEncode(gameVersion)}";

        using var response = await _httpClient
            .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !ModDbTrustPolicy.IsTrustedApiHost(finalUri))
        {
            throw new HttpRequestException("ModDB response originated from an unexpected host.");
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxApiResponseBytes)
        {
            throw new HttpRequestException("ModDB returned an unexpectedly large response body.");
        }

        var body = await ReadResponseBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ModDB returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(body, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, InstallInformation>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, InstallInformation>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in data.EnumerateObject())
        {
            result[property.Name] = InstallInformation.FromJson(property.Value);
        }

        return result;
    }

    private static ModUpdateStatus CreateStatus(InstalledMod mod, InstallInformation info)
    {
        if (info.ErrorCode is not null)
        {
            return ErrorStatus(mod, info.ErrorCode, info.Message ?? ErrorMessage(info.ErrorCode.Value));
        }

        if (!string.IsNullOrWhiteSpace(info.RecommendedUpgrade)
            && VersionComparer.IsNewer(info.RecommendedUpgrade, mod.Version))
        {
            return new ModUpdateStatus(
                mod.Identifier,
                mod.Version,
                ModUpdateKind.UpdateAvailable,
                info.RecommendedUpgrade,
                DownloadFileName: null,
                DownloadUrl: null,
                ErrorCode: null,
                $"Compatible update {info.RecommendedUpgrade} is available.");
        }

        return new ModUpdateStatus(
            mod.Identifier,
            mod.Version,
            ModUpdateKind.UpToDate,
            AvailableVersion: mod.Version,
            DownloadFileName: info.FileName,
            DownloadUrl: BuildDownloadUrl(info.FileUrl),
            ErrorCode: null,
            "This mod is up to date for the installed game version.");
    }

    private static ModUpdateStatus ErrorStatus(InstalledMod mod, int? errorCode, string message)
    {
        var kind = errorCode switch
        {
            4101 or 4102 => ModUpdateKind.Retracted,
            4031 => ModUpdateKind.NotFound,
            _ => ModUpdateKind.Error
        };

        return new ModUpdateStatus(
            mod.Identifier,
            mod.Version,
            kind,
            AvailableVersion: null,
            DownloadFileName: null,
            DownloadUrl: null,
            errorCode,
            message);
    }

    private static string BuildModSpec(InstalledMod mod, bool includeVersion)
    {
        return includeVersion && !string.IsNullOrWhiteSpace(mod.Version)
            ? $"{mod.Identifier}@{mod.Version}"
            : mod.Identifier;
    }

    private static string? BuildDownloadUrl(string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return null;
        }

        var resolvedUri = Uri.TryCreate(fileUrl, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(BaseUri, fileUrl);

        if (!ModDbTrustPolicy.IsTrustedDownloadEntryUri(resolvedUri))
        {
            return null;
        }

        return resolvedUri.ToString();
    }

    private static string ErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            4001 => "ModDB could not parse this mod identifier/version.",
            4002 => "The mod version is missing and no game version could be used.",
            4031 => "This mod could not be found on the official Vintage Story ModDB.",
            4032 => "This retracted release cannot be ignored.",
            4101 => "This release was retracted on ModDB.",
            4102 => "This release was force-retracted on ModDB.",
            _ => $"ModDB returned error code {errorCode}."
        };
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new MemoryStream();
        var buffer = new byte[81920];
        var totalBytes = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > MaxApiResponseBytes)
            {
                throw new HttpRequestException("ModDB response exceeded the supported size limit.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return System.Text.Encoding.UTF8.GetString(target.ToArray());
    }

    private static string? ReadLatestReleaseVersion(JsonElement modElement)
    {
        if (!TryGetProperty(modElement, "releases", out var releasesElement)
            || releasesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? latest = null;
        foreach (var release in releasesElement.EnumerateArray())
        {
            var version = ReadString(release, "modversion", "version");
            version = NormalizeVersion(version);
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            if (latest is null || VersionComparer.IsNewer(version, latest))
            {
                latest = version;
            }
        }

        return latest;
    }

    private static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        var separatorIndex = trimmed.IndexOfAny(new[] { '+', '-', ' ' });
        if (separatorIndex > 0)
        {
            trimmed = trimmed[..separatorIndex];
        }

        return Version.TryParse(trimmed, out _) ? trimmed : null;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record InstallInformation(
        string? FileName,
        string? FileUrl,
        string? RecommendedUpgrade,
        int? ErrorCode,
        string? Message)
    {
        public static InstallInformation FromJson(JsonElement element)
        {
            return new InstallInformation(
                ReadString(element, "fileName"),
                ReadString(element, "fileUrl"),
                ReadString(element, "recommendedUpgrade"),
                ReadInt(element, "errorCode"),
                ReadString(element, "retractionReason", "message", "error"));
        }

        private static string? ReadString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }

            return null;
        }

        private static int? ReadInt(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var property))
            {
                return null;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
            {
                return value;
            }

            return null;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
