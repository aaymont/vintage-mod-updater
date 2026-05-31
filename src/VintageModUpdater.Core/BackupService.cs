using System.Text.Json;

namespace VintageModUpdater.Core;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(string modsPath, CancellationToken cancellationToken = default)
    {
        var safeModsPath = PathGuard.NormalizePath(modsPath);
        var root = GetBackupRoot(safeModsPath);
        if (!Directory.Exists(root))
        {
            return Array.Empty<BackupEntry>();
        }

        var backups = new List<BackupEntry>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, "backup.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                PathGuard.EnsureNoReparsePointsUnderRoot(
                    root,
                    manifestPath,
                    "Refusing to read backup metadata through a symbolic link or junction path.");
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);

                if (manifest is not null)
                {
                    var entry = manifest.ToEntry();
                    if (IsValidBackupEntry(entry, safeModsPath, manifestPath))
                    {
                        backups.Add(entry);
                    }
                }
            }
            catch
            {
                // Ignore malformed backup manifests; the UI can still show valid backups.
            }
        }

        return backups
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();
    }

    public async Task<BackupEntry> CreateBackupAsync(
        InstalledMod mod,
        string modsPath,
        CancellationToken cancellationToken = default)
    {
        PathGuard.EnsureSafeModsPathForWrite(modsPath);
        var safeModsPath = PathGuard.NormalizePath(modsPath);
        PathGuard.EnsureContained(
            safeModsPath,
            mod.Path,
            "The selected mod path is outside the configured Mods directory.");
        PathGuard.EnsureNoReparsePointsUnderRoot(
            safeModsPath,
            mod.Path,
            "Refusing to back up mods through a symbolic link or junction path.");

        if (!File.Exists(mod.Path) && !Directory.Exists(mod.Path))
        {
            throw new FileNotFoundException("The installed mod could not be found for backup.", mod.Path);
        }

        var id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Sanitize(mod.Identifier)}_{Sanitize(mod.Version ?? "unknown")}";
        UpdaterWorkspace.EnsureWorkspace(safeModsPath);
        var backupRoot = GetBackupRoot(safeModsPath);
        PathGuard.EnsureNoReparsePointsUnderRoot(
            safeModsPath,
            backupRoot,
            "Refusing to back up mods through a symbolic link or junction path.");
        var backupDirectory = Path.Combine(backupRoot, id);
        PathGuard.EnsureNoReparsePointsUnderRoot(
            safeModsPath,
            backupDirectory,
            "Refusing to back up mods through a symbolic link or junction path.");
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(backupDirectory, mod.FileName);
        PathGuard.EnsureNoReparsePointsUnderRoot(
            safeModsPath,
            backupPath,
            "Refusing to back up mods through a symbolic link or junction path.");
        if (mod.IsDirectory)
        {
            CopyDirectory(
                mod.Path,
                backupPath,
                overwrite: true,
                sourceRootPath: safeModsPath,
                targetRootPath: backupRoot);
        }
        else
        {
            await CopyFileAsync(mod.Path, backupPath, overwrite: true, cancellationToken).ConfigureAwait(false);
        }

        var entry = new BackupEntry(
            id,
            mod.Identifier,
            mod.Name,
            mod.Version,
            PathGuard.NormalizePath(mod.Path),
            backupPath,
            mod.IsDirectory,
            DateTimeOffset.UtcNow);

        var manifest = BackupManifest.FromEntry(entry);
        var manifestPath = Path.Combine(backupDirectory, "backup.json");
        PathGuard.EnsureNoReparsePointsUnderRoot(
            safeModsPath,
            manifestPath,
            "Refusing to write backup metadata through a symbolic link or junction path.");
        await using var manifestStream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);

        return entry;
    }

    public async Task RestoreAsync(BackupEntry backup, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var safeBackupPath = PathGuard.NormalizePath(backup.BackupPath);
        var backupRootSegment = $"{Path.DirectorySeparatorChar}.vintage-mod-updater{Path.DirectorySeparatorChar}backups{Path.DirectorySeparatorChar}";
        var markerIndex = safeBackupPath.IndexOf(backupRootSegment, PathComparison);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("The backup path is not inside a valid updater backup directory.");
        }

        var modsPath = safeBackupPath[..markerIndex];
        var manifestPath = Path.Combine(
            Path.GetDirectoryName(safeBackupPath)
                ?? throw new InvalidOperationException("The backup path is not inside a valid updater backup directory."),
            "backup.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("The backup manifest could not be found.", manifestPath);
        }

        PathGuard.EnsureNoReparsePointsUnderRoot(
            GetBackupRoot(modsPath),
            manifestPath,
            "Refusing to read backup metadata through a symbolic link or junction path.");
        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
            manifestStream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            throw new InvalidOperationException("The backup manifest is invalid.");
        }

        var restoredEntry = manifest.ToEntry();
        if (!restoredEntry.Id.Equals(backup.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected backup metadata changed. Please rescan backups and try again.");
        }

        if (!string.Equals(restoredEntry.ModId, backup.ModId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(restoredEntry.ModName, backup.ModName, StringComparison.Ordinal)
            || !string.Equals(restoredEntry.Version, backup.Version, StringComparison.Ordinal)
            || !string.Equals(PathGuard.NormalizePath(restoredEntry.OriginalPath), PathGuard.NormalizePath(backup.OriginalPath), PathComparison)
            || !string.Equals(PathGuard.NormalizePath(restoredEntry.BackupPath), PathGuard.NormalizePath(backup.BackupPath), PathComparison)
            || restoredEntry.IsDirectory != backup.IsDirectory
            || restoredEntry.CreatedAt != backup.CreatedAt)
        {
            throw new InvalidOperationException("The selected backup metadata changed. Please rescan backups and try again.");
        }

        if (!IsValidBackupEntry(restoredEntry, modsPath, manifestPath))
        {
            throw new InvalidOperationException("The backup manifest contains invalid or unsafe paths.");
        }

        safeBackupPath = PathGuard.NormalizePath(restoredEntry.BackupPath);
        var safeOriginalPath = PathGuard.NormalizePath(restoredEntry.OriginalPath);
        if (!File.Exists(safeBackupPath) && !Directory.Exists(safeBackupPath))
        {
            throw new FileNotFoundException("The backup payload could not be found.", safeBackupPath);
        }

        PathGuard.EnsureSafeModsPathForWrite(modsPath);
        PathGuard.EnsureContained(
            modsPath,
            safeOriginalPath,
            "The backup restore target is outside the configured Mods directory.");
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            safeOriginalPath,
            "Refusing to restore mods through a symbolic link or junction path.");
        PathGuard.EnsureContained(
            GetBackupRoot(modsPath),
            safeBackupPath,
            "The backup payload is outside the configured backup directory.");
        PathGuard.EnsureNoReparsePointsUnderRoot(
            GetBackupRoot(modsPath),
            safeBackupPath,
            "Refusing to restore from a symbolic link or junction path.");

        var matchingInstalledMods = new ModScanner()
            .Scan(modsPath)
            .Where(mod => mod.Identifier.Equals(restoredEntry.ModId, StringComparison.OrdinalIgnoreCase))
            .Select(mod => mod.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in matchingInstalledMods)
        {
            PreservePath(path, modsPath);
        }

        PreservePath(safeOriginalPath, modsPath);

        if (restoredEntry.IsDirectory)
        {
            CopyDirectory(
                safeBackupPath,
                safeOriginalPath,
                overwrite: true,
                sourceRootPath: GetBackupRoot(modsPath),
                targetRootPath: modsPath);
        }
        else
        {
            PathGuard.EnsureNoReparsePointsUnderRoot(
                GetBackupRoot(modsPath),
                safeBackupPath,
                "Refusing to restore from a symbolic link or junction path.");
            PathGuard.EnsureNoReparsePointsUnderRoot(
                modsPath,
                safeOriginalPath,
                "Refusing to restore mods through a symbolic link or junction path.");
            File.Copy(safeBackupPath, safeOriginalPath, overwrite: true);
        }
    }

    public static string GetBackupRoot(string modsPath)
    {
        return Path.Combine(modsPath, ".vintage-mod-updater", "backups");
    }

    internal static async Task CopyFileAsync(
        string sourcePath,
        string targetPath,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        await using var source = File.OpenRead(sourcePath);
        await using var target = new FileStream(
            targetPath,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    internal static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        bool overwrite,
        string sourceRootPath,
        string targetRootPath)
    {
        PathGuard.EnsureNoReparsePointsUnderRoot(
            sourceRootPath,
            sourceDirectory,
            "Refusing to copy from a symbolic link or junction path.");
        PathGuard.EnsureNoReparsePointsUnderRoot(
            targetRootPath,
            targetDirectory,
            "Refusing to copy into a symbolic link or junction path.");
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            PathGuard.EnsureNoReparsePointsUnderRoot(
                sourceRootPath,
                file,
                "Refusing to copy from a symbolic link or junction path.");
            PathGuard.EnsureNoReparsePointsUnderRoot(
                targetRootPath,
                targetFile,
                "Refusing to copy into a symbolic link or junction path.");
            File.Copy(file, targetFile, overwrite);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var targetSubDirectory = Path.Combine(targetDirectory, Path.GetFileName(directory));
            PathGuard.EnsureNoReparsePointsUnderRoot(
                sourceRootPath,
                directory,
                "Refusing to copy from a symbolic link or junction path.");
            PathGuard.EnsureNoReparsePointsUnderRoot(
                targetRootPath,
                targetSubDirectory,
                "Refusing to copy into a symbolic link or junction path.");
            CopyDirectory(
                directory,
                targetSubDirectory,
                overwrite,
                sourceRootPath,
                targetRootPath);
        }
    }

    private static void PreservePath(string path, string modsPath)
    {
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            path,
            "Refusing to preserve files through a symbolic link or junction path.");

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        var preserveDirectory = Path.Combine(
            UpdaterWorkspace.EnsureWorkspace(modsPath),
            "replaced-on-restore",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            preserveDirectory,
            "Refusing to preserve files through a symbolic link or junction path.");
        Directory.CreateDirectory(preserveDirectory);

        var targetPath = Path.Combine(preserveDirectory, Path.GetFileName(path));
        PathGuard.EnsureNoReparsePointsUnderRoot(
            modsPath,
            targetPath,
            "Refusing to preserve files through a symbolic link or junction path.");
        if (Directory.Exists(path))
        {
            Directory.Move(path, targetPath);
        }
        else
        {
            File.Move(path, targetPath, overwrite: true);
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static bool IsValidBackupEntry(BackupEntry backup, string modsPath, string manifestPath)
    {
        try
        {
            var safeModsPath = PathGuard.NormalizePath(modsPath);
            var safeManifestPath = PathGuard.NormalizePath(manifestPath);
            var backupRoot = GetBackupRoot(safeModsPath);
            var safeOriginalPath = PathGuard.NormalizePath(backup.OriginalPath);
            var safeBackupPath = PathGuard.NormalizePath(backup.BackupPath);

            if (!PathGuard.IsPathContained(backupRoot, safeManifestPath))
            {
                return false;
            }

            if (!PathGuard.IsPathContained(safeModsPath, safeOriginalPath))
            {
                return false;
            }

            return PathGuard.IsPathContained(backupRoot, safeBackupPath);
        }
        catch
        {
            return false;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class BackupManifest
    {
        public string Id { get; set; } = "";

        public string ModId { get; set; } = "";

        public string ModName { get; set; } = "";

        public string? Version { get; set; }

        public string OriginalPath { get; set; } = "";

        public string BackupPath { get; set; } = "";

        public bool IsDirectory { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public BackupEntry ToEntry()
        {
            return new BackupEntry(Id, ModId, ModName, Version, OriginalPath, BackupPath, IsDirectory, CreatedAt);
        }

        public static BackupManifest FromEntry(BackupEntry entry)
        {
            return new BackupManifest
            {
                Id = entry.Id,
                ModId = entry.ModId,
                ModName = entry.ModName,
                Version = entry.Version,
                OriginalPath = entry.OriginalPath,
                BackupPath = entry.BackupPath,
                IsDirectory = entry.IsDirectory,
                CreatedAt = entry.CreatedAt
            };
        }
    }
}
