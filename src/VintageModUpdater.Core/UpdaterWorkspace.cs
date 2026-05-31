using System.Text.Json;

namespace VintageModUpdater.Core;

internal static class UpdaterWorkspace
{
    private const string WorkspaceDirectoryName = ".vintage-mod-updater";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Dictionary<string, object> PlaceholderModInfo = new(StringComparer.Ordinal)
    {
        ["type"] = "content",
        ["modid"] = "vintage_mod_updater_workspace",
        ["name"] = "Vintage Mod Updater Workspace",
        ["version"] = "0.0.0",
        ["description"] = "Placeholder metadata so Vintage Story ignores updater data folders.",
        ["authors"] = new[] { "Vintage Mod Updater" },
        ["side"] = "universal"
    };

    public static string EnsureWorkspace(string modsPath)
    {
        var safeModsPath = PathGuard.NormalizePath(modsPath);
        var workspacePath = Path.Combine(safeModsPath, WorkspaceDirectoryName);
        PathGuard.EnsureNoReparsePointsUnderRoot(
            safeModsPath,
            workspacePath,
            "Refusing to write updater workspace through a symbolic link or junction path.");
        Directory.CreateDirectory(workspacePath);
        EnsurePlaceholderModInfo(safeModsPath, workspacePath);
        return workspacePath;
    }

    private static void EnsurePlaceholderModInfo(string safeModsPath, string workspacePath)
    {
        var modInfoPath = Path.Combine(workspacePath, "modinfo.json");
        PathGuard.EnsureNoReparsePointsUnderRoot(
            safeModsPath,
            modInfoPath,
            "Refusing to write updater workspace metadata through a symbolic link or junction path.");

        if (File.Exists(modInfoPath))
        {
            return;
        }

        var json = JsonSerializer.Serialize(PlaceholderModInfo, JsonOptions);
        File.WriteAllText(modInfoPath, json);
    }
}
