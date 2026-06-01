namespace VintageModUpdater.Core;

public sealed class VintageModUpdaterService
{
    private readonly PathDiscoveryService _pathDiscoveryService;
    private readonly VintageStoryVersionReader _versionReader;
    private readonly ModScanner _modScanner;
    private readonly ModDbClient _modDbClient;
    private readonly BackupService _backupService;
    private readonly ModUpdateInstaller _modUpdateInstaller;

    public VintageModUpdaterService(
        PathDiscoveryService? pathDiscoveryService = null,
        VintageStoryVersionReader? versionReader = null,
        ModScanner? modScanner = null,
        ModDbClient? modDbClient = null,
        BackupService? backupService = null,
        ModUpdateInstaller? modUpdateInstaller = null)
    {
        _pathDiscoveryService = pathDiscoveryService ?? new PathDiscoveryService();
        _versionReader = versionReader ?? new VintageStoryVersionReader();
        _modScanner = modScanner ?? new ModScanner();
        _modDbClient = modDbClient ?? new ModDbClient();
        _backupService = backupService ?? new BackupService();
        _modUpdateInstaller = modUpdateInstaller ?? new ModUpdateInstaller(_backupService);
    }

    public async Task<ScanResult> ScanAsync(UpdaterSettings settings, CancellationToken cancellationToken = default)
    {
        var paths = _pathDiscoveryService.Discover(settings.InstallPath, settings.ModsPath);

        if (string.IsNullOrWhiteSpace(settings.InstallPath) && paths.InstallPathDetected)
        {
            settings.InstallPath = paths.InstallPath;
        }

        if (string.IsNullOrWhiteSpace(settings.ModsPath))
        {
            settings.ModsPath = paths.ModsPath;
        }

        var installPath = !string.IsNullOrWhiteSpace(settings.InstallPath)
            ? settings.InstallPath
            : paths.InstallPath;
        var detectedGameVersion = _versionReader.TryReadGameVersion(installPath);
        var gameVersion = ResolveGameVersion(detectedGameVersion, settings.GameVersionOverride);
        var modsPath = !string.IsNullOrWhiteSpace(settings.ModsPath)
            ? settings.ModsPath
            : paths.ModsPath;
        EnsureUpdaterWorkspaceForScan(modsPath);
        var mods = _modScanner.Scan(modsPath);
        var backups = await _backupService.ListBackupsAsync(modsPath, cancellationToken).ConfigureAwait(false);

        return new ScanResult(
            paths with
            {
                InstallPath = installPath,
                ModsPath = modsPath
            },
            detectedGameVersion,
            gameVersion,
            mods,
            backups);
    }

    public Task<IReadOnlyList<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        return _modDbClient.GetGameVersionsAsync(cancellationToken);
    }

    public Task<int?> TryResolveModAssetIdAsync(string modIdentifier, CancellationToken cancellationToken = default)
    {
        return _modDbClient.TryResolveAssetIdAsync(modIdentifier, cancellationToken);
    }

    public Task<ModDbModReference?> TryResolveModReferenceAsync(
        string modIdentifier,
        CancellationToken cancellationToken = default)
    {
        return _modDbClient.TryResolveModReferenceAsync(modIdentifier, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, int>> ResolveModAssetIdsAsync(
        IEnumerable<string> modIdentifiers,
        CancellationToken cancellationToken = default)
    {
        return _modDbClient.ResolveModAssetIdsAsync(modIdentifiers, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, ModDbModReference>> ResolveModReferencesAsync(
        IEnumerable<string> modIdentifiers,
        CancellationToken cancellationToken = default)
    {
        return _modDbClient.ResolveModReferencesAsync(modIdentifiers, cancellationToken);
    }

    public static string? ResolveGameVersion(string? detectedGameVersion, string? gameVersionOverride)
    {
        var normalizedOverride = string.IsNullOrWhiteSpace(gameVersionOverride)
            ? null
            : gameVersionOverride.Trim();
        return normalizedOverride ?? detectedGameVersion;
    }

    private static void EnsureUpdaterWorkspaceForScan(string modsPath)
    {
        try
        {
            UpdaterWorkspace.EnsureWorkspace(modsPath);
        }
        catch
        {
            // Workspace setup during scan is best-effort; scanning should continue.
        }
    }

    public Task<IReadOnlyDictionary<string, ModUpdateStatus>> CheckUpdatesAsync(
        ScanResult scanResult,
        CancellationToken cancellationToken = default)
    {
        return _modDbClient.CheckUpdatesAsync(scanResult.Mods, scanResult.GameVersion, cancellationToken);
    }

    public Task<AppUpdateStatus> CheckAppUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        return _modDbClient.CheckUpdaterAppUpdateAsync(currentVersion, cancellationToken);
    }

    public Task<ModUpdateInstallResult> UpdateModAsync(
        InstalledMod mod,
        ModUpdateStatus update,
        string modsPath,
        CancellationToken cancellationToken = default)
    {
        return _modUpdateInstaller.InstallUpdateAsync(mod, update, modsPath, cancellationToken);
    }

    public Task RestoreBackupAsync(BackupEntry backup, CancellationToken cancellationToken = default)
    {
        return _backupService.RestoreAsync(backup, cancellationToken);
    }
}
