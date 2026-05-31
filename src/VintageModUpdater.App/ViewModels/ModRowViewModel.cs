using System.ComponentModel;
using RuntimeMod = VintageModUpdater.Core.InstalledMod;
using VintageModUpdater.Core;

namespace VintageModUpdater.App.ViewModels;

public sealed class ModRowViewModel : INotifyPropertyChanged
{
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
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
