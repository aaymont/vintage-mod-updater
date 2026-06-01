using VintageModUpdater.Core;

namespace VintageModUpdater.App.ViewModels;

public sealed class BackupRowViewModel
{
    public BackupRowViewModel(BackupEntry backup)
    {
        Backup = backup;
    }

    public BackupEntry Backup { get; }

    public string Name => Backup.ModName;

    public string Identifier => Backup.ModId;

    public string Version => string.IsNullOrWhiteSpace(Backup.Version) ? "Unknown" : Backup.Version;

    public string CreatedAt => Backup.CreatedAt.LocalDateTime.ToString("g");

    public string SourceFile => Path.GetFileName(Backup.OriginalPath);

    public int? ModDbAssetId { get; private set; }

    public string? ModPageUrl => ModDbUrls.GetModPageUrl(ModDbAssetId);

    public void SetModDbAssetId(int? assetId)
    {
        ModDbAssetId = assetId;
    }
}
