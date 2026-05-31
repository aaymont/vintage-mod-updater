using System.IO.Compression;
using System.Text.Json;

namespace VintageModUpdater.Core;

public sealed class ModScanner
{
    private const long MaxModInfoBytes = 256 * 1024;
    private const long MaxCompressedModInfoBytes = 128 * 1024;
    private const int MaxZipEntryCount = 10000;

    public IReadOnlyList<InstalledMod> Scan(string modsPath)
    {
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
        {
            return Array.Empty<InstalledMod>();
        }

        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            modsPath,
            "Cannot scan mods through a symbolic link or junction path.");

        var mods = new List<InstalledMod>();

        foreach (var zipPath in Directory.EnumerateFiles(modsPath, "*.zip", SearchOption.TopDirectoryOnly))
        {
            PathGuard.EnsureNoReparsePointsUnderRoot(
                modsPath,
                zipPath,
                "Cannot scan mod entries behind a symbolic link or junction path.");

            mods.Add(ReadZipMod(zipPath));
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(modsPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(directoryPath).Equals(".vintage-mod-updater", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PathGuard.EnsureNoReparsePointsUnderRoot(
                modsPath,
                directoryPath,
                "Cannot scan mod entries behind a symbolic link or junction path.");

            var modInfoPath = Path.Combine(directoryPath, "modinfo.json");
            if (File.Exists(modInfoPath))
            {
                mods.Add(ReadDirectoryMod(directoryPath, modInfoPath));
            }
        }

        return mods
            .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(mod => mod.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static InstalledMod ReadZipMod(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count > MaxZipEntryCount)
            {
                return FailedMod(zipPath, isDirectory: false, "The mod zip contains too many entries.");
            }

            var modInfoEntries = FindModInfoEntries(archive).ToArray();
            if (modInfoEntries.Length > 1)
            {
                return FailedMod(zipPath, isDirectory: false, "The mod zip contains multiple modinfo.json entries.");
            }

            var entry = modInfoEntries.FirstOrDefault();

            if (entry is null)
            {
                return FailedMod(zipPath, isDirectory: false, "modinfo.json was not found in the mod zip.");
            }

            var root = ReadModInfoJson(entry);
            return FromModInfo(root, zipPath, isDirectory: false);
        }
        catch (Exception ex)
        {
            return FailedMod(zipPath, isDirectory: false, ex.Message);
        }
    }

    private static InstalledMod ReadDirectoryMod(string directoryPath, string modInfoPath)
    {
        try
        {
            if ((File.GetAttributes(modInfoPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("modinfo.json cannot be a symbolic link or junction.");
            }

            using var stream = File.OpenRead(modInfoPath);
            using var document = ParseModInfoDocument(stream);

            return FromModInfo(document.RootElement, directoryPath, isDirectory: true);
        }
        catch (Exception ex)
        {
            return FailedMod(directoryPath, isDirectory: true, ex.Message);
        }
    }

    internal static bool TryReadZipModIdentifier(string zipPath, out string? identifier, out string? error)
    {
        identifier = null;
        error = null;

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count > MaxZipEntryCount)
            {
                error = "The downloaded archive contains too many entries.";
                return false;
            }

            var modInfoEntries = FindModInfoEntries(archive).ToArray();
            if (modInfoEntries.Length > 1)
            {
                error = "The downloaded archive contains multiple modinfo.json entries.";
                return false;
            }

            var entry = modInfoEntries.FirstOrDefault();
            if (entry is null)
            {
                error = "modinfo.json was not found in the downloaded archive.";
                return false;
            }

            var root = ReadModInfoJson(entry);
            identifier = ReadString(root, "modid", "modId", "modID", "id");
            if (string.IsNullOrWhiteSpace(identifier))
            {
                error = "modinfo.json did not contain a valid mod identifier.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static InstalledMod FromModInfo(JsonElement root, string path, bool isDirectory)
    {
        var identifier = ReadString(root, "modid", "modId", "modID", "id")
            ?? Path.GetFileNameWithoutExtension(path);
        var name = ReadString(root, "name") ?? identifier;
        var version = ReadString(root, "version");
        var authors = ReadStringArray(root, "authors", "author").ToArray();
        var gameVersions = ReadStringArray(root, "gameversions", "gameVersions", "gameversion", "gameVersion").ToArray();

        return new InstalledMod(
            identifier,
            name,
            version,
            path,
            Path.GetFileName(path),
            isDirectory,
            authors,
            gameVersions,
            Error: null);
    }

    private static InstalledMod FailedMod(string path, bool isDirectory, string error)
    {
        var fileName = Path.GetFileName(path);
        var identifier = Path.GetFileNameWithoutExtension(path);

        return new InstalledMod(
            identifier,
            fileName,
            Version: null,
            path,
            fileName,
            isDirectory,
            Array.Empty<string>(),
            Array.Empty<string>(),
            error);
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return null;
    }

    private static IEnumerable<string> ReadStringArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Array)
            {
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
            }
            else if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }

            yield break;
        }
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
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

    private static JsonElement ReadModInfoJson(ZipArchiveEntry entry)
    {
        if (entry.CompressedLength > MaxCompressedModInfoBytes)
        {
            throw new InvalidOperationException("modinfo.json is too large in the compressed archive.");
        }

        if (entry.Length > MaxModInfoBytes)
        {
            throw new InvalidOperationException("modinfo.json is too large in the archive.");
        }

        using var stream = entry.Open();
        using var document = ParseModInfoDocument(stream);
        return document.RootElement.Clone();
    }

    private static JsonDocument ParseModInfoDocument(Stream stream)
    {
        using var boundedStream = CopyStreamWithLimit(stream, MaxModInfoBytes);
        return JsonDocument.Parse(boundedStream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
    }

    private static MemoryStream CopyStreamWithLimit(Stream source, long maxBytes)
    {
        var target = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maxBytes)
            {
                throw new InvalidOperationException("modinfo.json exceeded the supported size limit.");
            }

            target.Write(buffer, 0, read);
        }

        target.Position = 0;
        return target;
    }

    private static IEnumerable<ZipArchiveEntry> FindModInfoEntries(ZipArchive archive)
    {
        var rootEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase));
        if (rootEntry is not null)
        {
            yield return rootEntry;
            yield break;
        }

        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith("/modinfo.json", StringComparison.OrdinalIgnoreCase)
                     || entry.FullName.EndsWith("\\modinfo.json", StringComparison.OrdinalIgnoreCase)))
        {
            yield return entry;
        }
    }
}
