namespace VintageModUpdater.Core;

public sealed class ModUpdateInstaller
{
    private static readonly string OfficialModDbHost = "mods.vintagestory.at";

    private readonly HttpClient _httpClient;
    private readonly BackupService _backupService;

    public ModUpdateInstaller(BackupService backupService, HttpClient? httpClient = null)
    {
        _backupService = backupService;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VintageModUpdater/0.1");
    }

    public async Task<BackupEntry> InstallUpdateAsync(
        InstalledMod mod,
        ModUpdateStatus update,
        string modsPath,
        CancellationToken cancellationToken = default)
    {
        if (!update.HasUpdate)
        {
            throw new InvalidOperationException("This mod does not have a downloadable compatible update.");
        }

        var downloadUri = new Uri(update.DownloadUrl!);
        if (!downloadUri.Host.Equals(OfficialModDbHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Updates must be downloaded from the official Vintage Story ModDB.");
        }

        Directory.CreateDirectory(modsPath);

        var downloadFileName = Path.GetFileName(update.DownloadFileName);
        if (string.IsNullOrWhiteSpace(downloadFileName))
        {
            throw new InvalidOperationException("The ModDB update did not include a valid file name.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"vintage-mod-updater-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await _httpClient.GetAsync(downloadUri, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(tempPath);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            var backup = await _backupService.CreateBackupAsync(mod, modsPath, cancellationToken).ConfigureAwait(false);
            var destinationPath = Path.Combine(modsPath, downloadFileName);

            PreserveConflictingDestination(destinationPath, mod.Path, modsPath);
            RemoveInstalledMod(mod);

            await BackupService.CopyFileAsync(tempPath, destinationPath, overwrite: true, cancellationToken).ConfigureAwait(false);

            return backup;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void PreserveConflictingDestination(string destinationPath, string currentPath, string modsPath)
    {
        if (Path.GetFullPath(destinationPath).Equals(Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(destinationPath) && !Directory.Exists(destinationPath))
        {
            return;
        }

        var preserveDirectory = Path.Combine(
            modsPath,
            ".vintage-mod-updater",
            "replaced-on-update",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(preserveDirectory);

        var preservedPath = Path.Combine(preserveDirectory, Path.GetFileName(destinationPath));
        if (Directory.Exists(destinationPath))
        {
            Directory.Move(destinationPath, preservedPath);
        }
        else
        {
            File.Move(destinationPath, preservedPath, overwrite: true);
        }
    }

    private static void RemoveInstalledMod(InstalledMod mod)
    {
        if (mod.IsDirectory && Directory.Exists(mod.Path))
        {
            Directory.Delete(mod.Path, recursive: true);
            return;
        }

        if (File.Exists(mod.Path))
        {
            File.Delete(mod.Path);
        }
    }
}
