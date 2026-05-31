using System.IO.Compression;
using System.Text.Json;

namespace VintageModUpdater.Core;

public sealed class ModScanner
{
    public IReadOnlyList<InstalledMod> Scan(string modsPath)
    {
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
        {
            return Array.Empty<InstalledMod>();
        }

        var mods = new List<InstalledMod>();

        foreach (var zipPath in Directory.EnumerateFiles(modsPath, "*.zip", SearchOption.TopDirectoryOnly))
        {
            mods.Add(ReadZipMod(zipPath));
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(modsPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(directoryPath).Equals(".vintage-mod-updater", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
            var entry = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.EndsWith("/modinfo.json", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.EndsWith("\\modinfo.json", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return FailedMod(zipPath, isDirectory: false, "modinfo.json was not found in the mod zip.");
            }

            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            return FromModInfo(document.RootElement, zipPath, isDirectory: false);
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
            using var stream = File.OpenRead(modInfoPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            return FromModInfo(document.RootElement, directoryPath, isDirectory: true);
        }
        catch (Exception ex)
        {
            return FailedMod(directoryPath, isDirectory: true, ex.Message);
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
}
