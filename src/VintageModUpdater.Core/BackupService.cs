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
        var root = GetBackupRoot(modsPath);
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
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);

                if (manifest is not null)
                {
                    backups.Add(manifest.ToEntry());
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
        if (!File.Exists(mod.Path) && !Directory.Exists(mod.Path))
        {
            throw new FileNotFoundException("The installed mod could not be found for backup.", mod.Path);
        }

        var id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Sanitize(mod.Identifier)}_{Sanitize(mod.Version ?? "unknown")}";
        var backupDirectory = Path.Combine(GetBackupRoot(modsPath), id);
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(backupDirectory, mod.FileName);
        if (mod.IsDirectory)
        {
            CopyDirectory(mod.Path, backupPath, overwrite: true);
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
            mod.Path,
            backupPath,
            mod.IsDirectory,
            DateTimeOffset.UtcNow);

        var manifest = BackupManifest.FromEntry(entry);
        await using var manifestStream = File.Create(Path.Combine(backupDirectory, "backup.json"));
        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);

        return entry;
    }

    public Task RestoreAsync(BackupEntry backup, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(backup.BackupPath) && !Directory.Exists(backup.BackupPath))
        {
            throw new FileNotFoundException("The backup payload could not be found.", backup.BackupPath);
        }

        var modsPath = Path.GetDirectoryName(backup.OriginalPath)
            ?? throw new InvalidOperationException("The original mod path is invalid.");

        var matchingInstalledMods = new ModScanner()
            .Scan(modsPath)
            .Where(mod => mod.Identifier.Equals(backup.ModId, StringComparison.OrdinalIgnoreCase))
            .Select(mod => mod.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in matchingInstalledMods)
        {
            PreservePath(path, modsPath);
        }

        PreservePath(backup.OriginalPath, modsPath);

        if (backup.IsDirectory)
        {
            CopyDirectory(backup.BackupPath, backup.OriginalPath, overwrite: true);
        }
        else
        {
            File.Copy(backup.BackupPath, backup.OriginalPath, overwrite: true);
        }

        return Task.CompletedTask;
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

    internal static void CopyDirectory(string sourceDirectory, string targetDirectory, bool overwrite)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)), overwrite);
        }
    }

    private static void PreservePath(string path, string modsPath)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        var preserveDirectory = Path.Combine(
            modsPath,
            ".vintage-mod-updater",
            "replaced-on-restore",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(preserveDirectory);

        var targetPath = Path.Combine(preserveDirectory, Path.GetFileName(path));
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
