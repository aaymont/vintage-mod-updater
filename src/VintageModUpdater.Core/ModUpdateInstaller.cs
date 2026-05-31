namespace VintageModUpdater.Core;

public sealed class ModUpdateInstaller
{
    private const long MaxDownloadBytes = 512L * 1024L * 1024L;
    private static readonly TimeSpan MaxDownloadTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly BackupService _backupService;

    public ModUpdateInstaller(BackupService backupService, HttpClient? httpClient = null)
    {
        _backupService = backupService;
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout > MaxDownloadTimeout)
        {
            _httpClient.Timeout = MaxDownloadTimeout;
        }

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VintageModUpdater/0.1");
    }

    public async Task<ModUpdateInstallResult> InstallUpdateAsync(
        InstalledMod mod,
        ModUpdateStatus update,
        string modsPath,
        CancellationToken cancellationToken = default)
    {
        using var log = UpdateOperationLog.Create(modsPath, mod.Identifier);
        log.WriteStep($"Starting update for {mod.Name} ({mod.Identifier})");
        log.WriteStep($"Current install: {mod.Path}");
        log.WriteStep($"Current version: {mod.Version ?? "unknown"}");
        log.WriteStep($"Target version: {update.AvailableVersion ?? "unknown"}");
        log.WriteStep($"Download URL: {update.DownloadUrl ?? "(missing)"}");
        log.WriteStep($"Download file name: {update.DownloadFileName ?? "(missing)"}");

        try
        {
            if (!update.HasUpdate)
            {
                throw new InvalidOperationException("This mod does not have a downloadable compatible update.");
            }

            var downloadUri = new Uri(update.DownloadUrl!);
            if (!ModDbTrustPolicy.IsTrustedDownloadEntryUri(downloadUri))
            {
                throw new InvalidOperationException("Updates must be downloaded from the official Vintage Story ModDB.");
            }

            PathGuard.EnsureSafeModsPathForWrite(modsPath);
            var safeModsPath = PathGuard.NormalizePath(modsPath);
            log.WriteStep($"Mods directory: {safeModsPath}");
            PathGuard.EnsureContained(
                safeModsPath,
                mod.Path,
                "The selected mod path is outside the configured Mods directory.");
            PathGuard.EnsureNoReparsePointsUnderRoot(
                safeModsPath,
                mod.Path,
                "Refusing to update mods through a symbolic link or junction path.");
            Directory.CreateDirectory(safeModsPath);

            var downloadFileName = Path.GetFileName(update.DownloadFileName);
            if (string.IsNullOrWhiteSpace(downloadFileName))
            {
                throw new InvalidOperationException("The ModDB update did not include a valid file name.");
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"vintage-mod-updater-{Guid.NewGuid():N}.zip");
            log.WriteStep($"Temporary download path: {tempPath}");
            try
            {
                log.WriteStep("Downloading update from ModDB...");
                using (var response = await _httpClient
                    .GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var finalUri = response.RequestMessage?.RequestUri ?? downloadUri;
                    log.WriteStep($"Download resolved to: {finalUri}");
                    if (!ModDbTrustPolicy.IsTrustedDownloadFinalUri(finalUri))
                    {
                        throw new InvalidOperationException(
                            "Update downloads must resolve to an official Vintage Story ModDB host.");
                    }

                    if (response.Content.Headers.ContentLength is long contentLength
                        && contentLength > MaxDownloadBytes)
                    {
                        throw new InvalidOperationException("The update download is larger than the supported size limit.");
                    }

                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await CopyToTempFileWithLimitAsync(source, tempPath, cancellationToken).ConfigureAwait(false);
                }

                log.WriteStep("Download completed. Validating archive...");
                var installedVersion = ValidateDownloadedArchive(mod.Identifier, update.AvailableVersion, tempPath);
                log.WriteStep($"Archive validated. Version in archive: {installedVersion ?? "unknown"}");

                log.WriteStep("Creating backup of the installed mod...");
                var backup = await _backupService.CreateBackupAsync(mod, safeModsPath, cancellationToken).ConfigureAwait(false);
                log.WriteStep($"Backup created at: {backup.BackupPath}");

                var destinationPath = Path.Combine(safeModsPath, downloadFileName);
                log.WriteStep($"Install destination: {destinationPath}");
                PathGuard.EnsureContained(
                    safeModsPath,
                    destinationPath,
                    "The resolved destination path is outside the configured Mods directory.");
                PathGuard.EnsureNoReparsePointsUnderRoot(
                    safeModsPath,
                    destinationPath,
                    "Refusing to update mods through a symbolic link or junction path.");

                PreserveConflictingDestination(destinationPath, mod.Path, safeModsPath, log);
                log.WriteStep($"Removing previous install at: {mod.Path}");
                RemoveInstalledMod(mod, safeModsPath);

                log.WriteStep("Copying validated update into the Mods directory...");
                await BackupService.CopyFileAsync(tempPath, destinationPath, overwrite: true, cancellationToken).ConfigureAwait(false);
                log.WriteStep("Install copy completed.");

                if (!File.Exists(destinationPath))
                {
                    throw new InvalidOperationException(
                        $"The update appeared to finish, but the destination file was not found: {destinationPath}");
                }

                log.WriteSuccess(destinationPath, installedVersion);
                return new ModUpdateInstallResult(backup, destinationPath, installedVersion, log.LogPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                    log.WriteStep("Temporary download file deleted.");
                }
            }
        }
        catch (Exception ex)
        {
            log.WriteFailure(ex);
            ex.Data["LogPath"] = log.LogPath;
            throw;
        }
    }

    private static void PreserveConflictingDestination(
        string destinationPath,
        string currentPath,
        string modsPath,
        UpdateOperationLog log)
    {
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            destinationPath,
            "Refusing to preserve a conflicting destination through a symbolic link or junction path.");

        if (Path.GetFullPath(destinationPath).Equals(Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
        {
            log.WriteStep("Install destination matches the current mod path.");
            return;
        }

        if (!File.Exists(destinationPath) && !Directory.Exists(destinationPath))
        {
            log.WriteStep("No conflicting destination file or folder found.");
            return;
        }

        var preserveDirectory = Path.Combine(
            modsPath,
            ".vintage-mod-updater",
            "replaced-on-update",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            preserveDirectory,
            "Refusing to preserve files through a symbolic link or junction path.");
        Directory.CreateDirectory(preserveDirectory);

        var preservedPath = Path.Combine(preserveDirectory, Path.GetFileName(destinationPath));
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            preservedPath,
            "Refusing to preserve files through a symbolic link or junction path.");
        if (Directory.Exists(destinationPath))
        {
            Directory.Move(destinationPath, preservedPath);
        }
        else
        {
            File.Move(destinationPath, preservedPath, overwrite: true);
        }

        log.WriteStep($"Preserved conflicting destination at: {preservedPath}");
    }

    private static void RemoveInstalledMod(InstalledMod mod, string modsPath)
    {
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            mod.Path,
            "Refusing to delete mods through a symbolic link or junction path.");

        if (mod.IsDirectory && Directory.Exists(mod.Path))
        {
            DeleteDirectorySafely(mod.Path, modsPath);
            return;
        }

        if (File.Exists(mod.Path))
        {
            File.Delete(mod.Path);
        }
    }

    private static void DeleteDirectorySafely(string directoryPath, string modsPath)
    {
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            directoryPath,
            "Refusing to delete mods through a symbolic link or junction path.");

        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            PathGuard.EnsureNoReparsePointsUnderRoot(
                modsPath,
                filePath,
                "Refusing to delete mods through a symbolic link or junction path.");
            File.Delete(filePath);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath))
        {
            PathGuard.EnsureNoReparsePointsUnderRoot(
                modsPath,
                childDirectory,
                "Refusing to delete mods through a symbolic link or junction path.");
            DeleteDirectorySafely(childDirectory, modsPath);
        }

        Directory.Delete(directoryPath, recursive: false);
    }

    private static async Task CopyToTempFileWithLimitAsync(Stream source, string tempPath, CancellationToken cancellationToken)
    {
        await using var target = File.Create(tempPath);
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > MaxDownloadBytes)
            {
                throw new InvalidOperationException("The update download exceeded the supported size limit.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? ValidateDownloadedArchive(
        string expectedModId,
        string? expectedVersion,
        string downloadedArchivePath)
    {
        if (!ModScanner.TryReadZipModMetadata(
                downloadedArchivePath,
                out var archiveModId,
                out var archiveVersion,
                out var validationError))
        {
            throw new InvalidOperationException(
                $"The downloaded mod archive is invalid: {validationError ?? "modinfo.json validation failed."}");
        }

        if (!string.Equals(expectedModId, archiveModId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The downloaded archive targets '{archiveModId}', but '{expectedModId}' was requested.");
        }

        if (string.IsNullOrWhiteSpace(archiveVersion))
        {
            throw new InvalidOperationException("The downloaded mod archive did not include a version in modinfo.json.");
        }

        if (!string.IsNullOrWhiteSpace(expectedVersion)
            && VersionComparer.Compare(archiveVersion, expectedVersion) != 0)
        {
            throw new InvalidOperationException(
                $"The downloaded archive version '{archiveVersion}' does not match the expected update version '{expectedVersion}'.");
        }

        return archiveVersion;
    }
}
