using System.Text;

namespace VintageModUpdater.Core;

public sealed record ModSnapshotEntry(string ModId, string? Version, bool Update);

public static class ModSnapshotCsv
{
    public const string Header = "modid,version,update";

    public static string Build(IEnumerable<ModSnapshotEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);

        foreach (var entry in entries)
        {
            builder.Append(Escape(entry.ModId));
            builder.Append(',');
            builder.Append(Escape(entry.Version ?? ""));
            builder.Append(',');
            builder.Append(entry.Update ? "true" : "false");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static IReadOnlyList<ModSnapshotEntry> CreateEntriesFromInstalledMods(
        IEnumerable<InstalledMod> mods,
        bool defaultUpdate = true)
    {
        return mods
            .Where(mod => mod.Error is null && !string.IsNullOrWhiteSpace(mod.Identifier))
            .GroupBy(mod => mod.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(mod => mod.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(mod => new ModSnapshotEntry(
                mod.Identifier.Trim(),
                string.IsNullOrWhiteSpace(mod.Version) ? null : mod.Version.Trim(),
                defaultUpdate))
            .ToArray();
    }

    internal static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }
}
