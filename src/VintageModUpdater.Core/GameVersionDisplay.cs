namespace VintageModUpdater.Core;

public static class GameVersionDisplay
{
    public static string? FormatRange(IEnumerable<string>? versions)
    {
        if (versions is null)
        {
            return null;
        }

        var sorted = versions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => version.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(version => version, Comparer<string>.Create(VersionComparer.Compare))
            .ToArray();

        if (sorted.Length == 0)
        {
            return null;
        }

        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        return $"{sorted[0]} - {sorted[^1]}";
    }
}
