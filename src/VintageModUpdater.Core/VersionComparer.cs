using System.Text.RegularExpressions;

namespace VintageModUpdater.Core;

public static partial class VersionComparer
{
    public static bool IsNewer(string? candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return true;
        }

        return Compare(candidate, current) > 0;
    }

    public static int Compare(string? left, string? right)
    {
        var leftVersion = ParsedVersion.Parse(left);
        var rightVersion = ParsedVersion.Parse(right);

        for (var i = 0; i < Math.Max(leftVersion.Numbers.Length, rightVersion.Numbers.Length); i++)
        {
            var leftPart = i < leftVersion.Numbers.Length ? leftVersion.Numbers[i] : 0;
            var rightPart = i < rightVersion.Numbers.Length ? rightVersion.Numbers[i] : 0;

            if (leftPart != rightPart)
            {
                return leftPart.CompareTo(rightPart);
            }
        }

        if (string.Equals(leftVersion.PreRelease, rightVersion.PreRelease, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (leftVersion.PreRelease is null)
        {
            return 1;
        }

        if (rightVersion.PreRelease is null)
        {
            return -1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(leftVersion.PreRelease, rightVersion.PreRelease);
    }

    private sealed record ParsedVersion(int[] Numbers, string? PreRelease)
    {
        public static ParsedVersion Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new ParsedVersion(Array.Empty<int>(), null);
            }

            var match = VersionPattern().Match(value);
            if (!match.Success)
            {
                return new ParsedVersion(Array.Empty<int>(), value);
            }

            var numbers = match.Groups["numbers"].Value
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part, out var number) ? number : 0)
                .ToArray();
            var preRelease = match.Groups["pre"].Success ? match.Groups["pre"].Value.TrimStart('-', '.') : null;

            return new ParsedVersion(numbers, string.IsNullOrWhiteSpace(preRelease) ? null : preRelease);
        }
    }

    [GeneratedRegex(@"(?<numbers>\d+(?:\.\d+)*)(?<pre>[-.][A-Za-z][A-Za-z0-9.-]*)?")]
    private static partial Regex VersionPattern();
}
