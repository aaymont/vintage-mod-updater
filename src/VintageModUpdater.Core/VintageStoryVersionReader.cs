using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace VintageModUpdater.Core;

public sealed partial class VintageStoryVersionReader
{
    public string? TryReadGameVersion(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return null;
        }

        foreach (var file in GetVersionCandidateFiles(installPath))
        {
            var version = TryReadVersionFromFile(file);
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetVersionCandidateFiles(string installPath)
    {
        var directCandidates = new[]
        {
            "Vintagestory.exe",
            "Vintagestory.dll",
            "VintagestoryLib.dll",
            Path.Combine("Contents", "MacOS", "Vintagestory"),
            Path.Combine("Contents", "Resources", "Vintagestory.dll")
        };

        foreach (var candidate in directCandidates.Select(candidate => Path.Combine(installPath, candidate)))
        {
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var file in Directory.EnumerateFiles(installPath, "Vintagestory*.dll", SearchOption.TopDirectoryOnly))
        {
            yield return file;
        }
    }

    private static string? TryReadVersionFromFile(string file)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            var version = NormalizeVersion(info.ProductVersion)
                ?? NormalizeVersion(info.FileVersion);

            if (version is not null)
            {
                return version;
            }
        }
        catch
        {
            // Some Linux/macOS entries may not expose PE-style version information.
        }

        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(file);
            return NormalizeVersion(assemblyName.Version?.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = VersionPattern().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var candidate = match.Value;
        var suffixStart = candidate.IndexOf('-');
        var numericPart = suffixStart >= 0 ? candidate[..suffixStart] : candidate;
        var suffix = suffixStart >= 0 ? candidate[suffixStart..] : "";
        var numericSegments = numericPart.Split('.');

        if (numericSegments.Length > 3)
        {
            numericPart = string.Join('.', numericSegments.Take(3));
        }

        return numericPart + suffix;
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+(?:\.\d+)?(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?")]
    private static partial Regex VersionPattern();
}
