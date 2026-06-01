using System.ComponentModel;
using RuntimeMod = VintageModUpdater.Core.InstalledMod;
using VintageModUpdater.Core;

namespace VintageModUpdater.App.ViewModels;

public sealed class ModRowViewModel : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, string>? _releaseGameVersionsByModVersion;

    public ModRowViewModel(RuntimeMod mod)
    {
        Mod = mod;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RuntimeMod Mod { get; }

    public ModUpdateStatus? UpdateStatus { get; private set; }

    public string Name => Mod.Name;

    public string Identifier => Mod.Identifier;

    public string Version => string.IsNullOrWhiteSpace(Mod.Version) ? "Unknown" : Mod.Version;

    public string FileName => Mod.FileName;

    public int? ModDbAssetId { get; private set; }

    public string? ModPageUrl => ModDbUrls.GetModPageUrl(ModDbAssetId);

    public string? InstalledForGameVersionText => ResolveInstalledForGameVersionText();

    public string? UpdateForGameVersionText => ResolveUpdateForGameVersionText();

    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Mod.Error))
            {
                return Mod.Error;
            }

            return UpdateStatus?.Kind switch
            {
                ModUpdateKind.UpToDate => "Up to date",
                ModUpdateKind.UpdateAvailable => $"Update available: {UpdateStatus.AvailableVersion}",
                ModUpdateKind.MissingGameVersion => "Needs game version",
                ModUpdateKind.NotFound => "Not found on ModDB",
                ModUpdateKind.Retracted => "Release retracted",
                ModUpdateKind.Error => UpdateStatus.Message ?? "Update check failed",
                _ => "Not checked"
            };
        }
    }

    public bool CanUpdate => UpdateStatus?.HasUpdate == true;

    public string UpdateButtonText => CanUpdate ? $"Update to {UpdateStatus!.AvailableVersion}" : "Update";

    public void ApplyUpdateStatus(ModUpdateStatus? status)
    {
        UpdateStatus = status;
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(UpdateForGameVersionText));
    }

    public void SetModDbReference(ModDbModReference reference)
    {
        SetModDbAssetId(reference.AssetId);
        _releaseGameVersionsByModVersion = reference.ReleaseGameVersionsByModVersion;
        OnPropertyChanged(nameof(InstalledForGameVersionText));
        OnPropertyChanged(nameof(UpdateForGameVersionText));
    }

    public void SetModDbAssetId(int? assetId)
    {
        if (ModDbAssetId == assetId)
        {
            return;
        }

        ModDbAssetId = assetId;
        OnPropertyChanged(nameof(ModDbAssetId));
        OnPropertyChanged(nameof(ModPageUrl));
    }

    private string? ResolveInstalledForGameVersionText()
    {
        var fromModInfo = GameVersionDisplay.FormatRange(Mod.GameVersions);
        if (!string.IsNullOrWhiteSpace(fromModInfo))
        {
            return fromModInfo;
        }

        return LookupReleaseGameVersions(Mod.Version);
    }

    private string? ResolveUpdateForGameVersionText()
    {
        if (UpdateStatus?.Kind != ModUpdateKind.UpdateAvailable)
        {
            return null;
        }

        return LookupReleaseGameVersions(UpdateStatus.AvailableVersion);
    }

    private string? LookupReleaseGameVersions(string? modVersion)
    {
        if (string.IsNullOrWhiteSpace(modVersion)
            || _releaseGameVersionsByModVersion is null)
        {
            return null;
        }

        return _releaseGameVersionsByModVersion.TryGetValue(modVersion.Trim(), out var formatted)
            ? formatted
            : null;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
