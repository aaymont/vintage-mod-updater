namespace VintageModUpdater.Core;

public enum ModUpdateKind
{
    Unknown,
    UpToDate,
    UpdateAvailable,
    MissingGameVersion,
    NotFound,
    Retracted,
    Error
}

public sealed record VintageStoryPaths(
    string? InstallPath,
    string? DataPath,
    string ModsPath,
    bool InstallPathDetected,
    bool DataPathDetected,
    IReadOnlyList<string> InstallPathCandidates,
    IReadOnlyList<string> DataPathCandidates);

public sealed record InstalledMod(
    string Identifier,
    string Name,
    string? Version,
    string Path,
    string FileName,
    bool IsDirectory,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> GameVersions,
    string? Error)
{
    public bool CanCheckForUpdates => !string.IsNullOrWhiteSpace(Identifier) && Error is null;
}

public sealed record ModUpdateStatus(
    string ModId,
    string? CurrentVersion,
    ModUpdateKind Kind,
    string? AvailableVersion,
    string? DownloadFileName,
    string? DownloadUrl,
    int? ErrorCode,
    string? Message)
{
    public bool HasUpdate => Kind == ModUpdateKind.UpdateAvailable
        && !string.IsNullOrWhiteSpace(AvailableVersion)
        && !string.IsNullOrWhiteSpace(DownloadUrl)
        && !string.IsNullOrWhiteSpace(DownloadFileName);
}

public sealed record BackupEntry(
    string Id,
    string ModId,
    string ModName,
    string? Version,
    string OriginalPath,
    string BackupPath,
    bool IsDirectory,
    DateTimeOffset CreatedAt);

public sealed record ModUpdateInstallResult(
    BackupEntry Backup,
    string DestinationPath,
    string? InstalledVersion,
    string LogPath);

public sealed record ScanResult(
    VintageStoryPaths Paths,
    string? DetectedGameVersion,
    string? GameVersion,
    IReadOnlyList<InstalledMod> Mods,
    IReadOnlyList<BackupEntry> Backups);

public sealed record AppUpdateStatus(
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string ModPageUrl);

public sealed class UpdaterSettings
{
    public string? InstallPath { get; set; }

    public string? ModsPath { get; set; }

    public string? GameVersionOverride { get; set; }
}
