using System.Runtime.InteropServices;

namespace VintageModUpdater.Core;

public sealed class PathDiscoveryService
{
    public VintageStoryPaths Discover(string? configuredInstallPath = null, string? configuredModsPath = null)
    {
        var installCandidates = GetInstallCandidates().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var dataCandidates = GetDataCandidates().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var configuredInstallExists = !string.IsNullOrWhiteSpace(configuredInstallPath)
            && Directory.Exists(configuredInstallPath);

        var detectedInstallPath = configuredInstallExists
            ? configuredInstallPath
            : installCandidates.FirstOrDefault(LooksLikeVintageStoryInstall);

        var detectedDataPath = dataCandidates.FirstOrDefault(Directory.Exists);
        var configuredModsExists = !string.IsNullOrWhiteSpace(configuredModsPath)
            && Directory.Exists(configuredModsPath);

        var modsPath = configuredModsExists
            ? configuredModsPath!
            : detectedDataPath is not null
                ? System.IO.Path.Combine(detectedDataPath, "Mods")
                : BuildDefaultDataPath("Mods");

        return new VintageStoryPaths(
            Normalize(detectedInstallPath),
            Normalize(detectedDataPath),
            Normalize(modsPath)!,
            detectedInstallPath is not null && LooksLikeVintageStoryInstall(detectedInstallPath),
            detectedDataPath is not null,
            installCandidates.Select(Normalize).WhereNotNull().ToArray(),
            dataCandidates.Select(Normalize).WhereNotNull().ToArray());
    }

    public static bool LooksLikeVintageStoryInstall(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var markers = new[]
        {
            "Vintagestory.exe",
            "Vintagestory.dll",
            "Vintagestory",
            System.IO.Path.Combine("Contents", "MacOS", "Vintagestory")
        };

        return markers.Any(marker => File.Exists(System.IO.Path.Combine(path, marker)))
            || Directory.Exists(System.IO.Path.Combine(path, "assets"));
    }

    private static IEnumerable<string> GetInstallCandidates()
    {
        var vintageStoryEnv = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        if (!string.IsNullOrWhiteSpace(vintageStoryEnv))
        {
            yield return vintageStoryEnv;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            yield return System.IO.Path.Combine(appData, "Vintagestory");
            yield return System.IO.Path.Combine(localAppData, "Programs", "Vintagestory");
            yield return System.IO.Path.Combine(programFiles, "Vintagestory");
            yield return System.IO.Path.Combine(programFilesX86, "Vintagestory");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/vintagestory.app";
            yield return "/Applications/Vintagestory.app";
            yield return System.IO.Path.Combine(home, "Applications", "vintagestory.app");
            yield return System.IO.Path.Combine(home, "Applications", "Vintagestory.app");
            yield return System.IO.Path.Combine(home, ".config", "Vintagestory");
        }
        else
        {
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdgConfig))
            {
                yield return System.IO.Path.Combine(xdgConfig, "Vintagestory");
            }

            yield return System.IO.Path.Combine(home, ".local", "share", "vintagestory");
            yield return System.IO.Path.Combine(home, ".config", "Vintagestory");
            yield return System.IO.Path.Combine(home, ".var", "app", "at.vintagestory.VintageStory");
        }
    }

    private static IEnumerable<string> GetDataCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return System.IO.Path.Combine(appData, "VintagestoryData");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return System.IO.Path.Combine(home, "Library", "Application Support", "VintagestoryData");
            yield return System.IO.Path.Combine(home, ".config", "VintagestoryData");
        }
        else
        {
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdgConfig))
            {
                yield return System.IO.Path.Combine(xdgConfig, "VintagestoryData");
            }

            yield return System.IO.Path.Combine(home, ".config", "VintagestoryData");
            yield return System.IO.Path.Combine(home, ".var", "app", "at.vintagestory.VintageStory", "config", "VintagestoryData");
        }
    }

    private static string BuildDefaultDataPath(string child)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VintagestoryData",
                child);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return System.IO.Path.Combine(home, "Library", "Application Support", "VintagestoryData", child);
        }

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configRoot = string.IsNullOrWhiteSpace(xdgConfig)
            ? System.IO.Path.Combine(home, ".config")
            : xdgConfig;

        return System.IO.Path.Combine(configRoot, "VintagestoryData", child);
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}

internal static class EnumerableExtensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
    {
        foreach (var item in source)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }
}
