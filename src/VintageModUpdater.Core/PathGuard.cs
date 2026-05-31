using System.Runtime.InteropServices;

namespace VintageModUpdater.Core;

internal static class PathGuard
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("A required path value is missing.");
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    public static bool IsPathContained(string rootPath, string candidatePath)
    {
        var normalizedRoot = NormalizePath(rootPath);
        var root = EnsureTrailingSeparator(normalizedRoot);
        var candidate = NormalizePath(candidatePath);
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return candidate.Equals(normalizedRoot, comparison)
            || candidate.StartsWith(root, comparison);
    }

    public static void EnsureSafeModsPathForWrite(string modsPath)
    {
        var normalized = NormalizePath(modsPath);
        var pathRoot = Path.GetPathRoot(normalized);
        var normalizedWithoutTrailing = TrimTrailingSeparators(normalized);

        if (string.IsNullOrWhiteSpace(pathRoot))
        {
            throw new InvalidOperationException("Refusing to modify files at a filesystem root path.");
        }

        var normalizedRoot = NormalizePath(pathRoot);
        var normalizedRootWithoutTrailing = TrimTrailingSeparators(normalizedRoot);
        if (normalizedWithoutTrailing.Equals(normalizedRootWithoutTrailing, PathComparison))
        {
            throw new InvalidOperationException("Refusing to modify files at a filesystem root path.");
        }

        if (!Path.GetFileName(normalizedWithoutTrailing).Equals("Mods", PathComparison))
        {
            throw new InvalidOperationException("For safety, updates are only allowed when the selected folder is named 'Mods'.");
        }
    }

    public static void EnsureContained(string rootPath, string candidatePath, string message)
    {
        if (!IsPathContained(rootPath, candidatePath))
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void EnsureNoReparsePointsUnderRoot(string rootPath, string candidatePath, string message)
    {
        var root = NormalizePath(rootPath);
        var candidate = NormalizePath(candidatePath);
        EnsureContained(root, candidate, message);

        if (HasReparsePoint(root))
        {
            throw new InvalidOperationException(message);
        }

        var relative = Path.GetRelativePath(root, candidate);
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
        {
            return;
        }

        var current = root;
        foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (HasReparsePoint(current))
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    private static string EnsureTrailingSeparator(string value)
    {
        if (value.EndsWith(Path.DirectorySeparatorChar)
            || value.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return value;
        }

        return value + Path.DirectorySeparatorChar;
    }

    private static string TrimTrailingSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? path : trimmed;
    }

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool HasReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}
