using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using VintageModUpdater.Core;

namespace VintageModUpdater.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public const string AutoGameVersionLabel = "Auto (from installation)";

    private readonly SettingsStore _settingsStore;
    private readonly VintageModUpdaterService _updaterService;
    private UpdaterSettings _settings = new();
    private ScanResult? _scanResult;
    private bool _isBusy;
    private string _statusMessage = "Ready";
    private string? _detectedGameVersion;
    private string? _gameVersion;
    private bool _isAppUpdateAvailable;
    private string _appUpdateBannerText = "";
    private string _officialAppModPageUrl = "https://mods.vintagestory.at/vsmu";
    private IReadOnlyList<string> _gameVersionOptions = Array.Empty<string>();

    public MainViewModel()
        : this(new SettingsStore(), new VintageModUpdaterService())
    {
    }

    public MainViewModel(SettingsStore settingsStore, VintageModUpdaterService updaterService)
    {
        _settingsStore = settingsStore;
        _updaterService = updaterService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ModRowViewModel> Mods { get; } = new();

    public ObservableCollection<BackupRowViewModel> Backups { get; } = new();

    public IReadOnlyList<string> GameVersionOptions => _gameVersionOptions;

    public bool CanExportSnapshot => Mods.Any(mod => mod.Mod.Error is null && !string.IsNullOrWhiteSpace(mod.Identifier));

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    public string InstallPath
    {
        get => _settings.InstallPath ?? "";
        set
        {
            var normalized = NormalizeInput(value);
            if (_settings.InstallPath == normalized)
            {
                return;
            }

            _settings.InstallPath = normalized;
            OnPropertyChanged(nameof(InstallPath));
        }
    }

    public string ModsPath
    {
        get => _settings.ModsPath ?? "";
        set
        {
            var normalized = NormalizeInput(value);
            if (_settings.ModsPath == normalized)
            {
                return;
            }

            _settings.ModsPath = normalized;
            OnPropertyChanged(nameof(ModsPath));
        }
    }

    public string GameVersion
    {
        get => string.IsNullOrWhiteSpace(_gameVersion) ? "Unknown" : _gameVersion;
        private set
        {
            if (_gameVersion == value)
            {
                return;
            }

            _gameVersion = value;
            OnPropertyChanged(nameof(GameVersion));
            OnPropertyChanged(nameof(IsGameVersionOverridden));
            OnPropertyChanged(nameof(GameVersionHeaderText));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string DetectedGameVersion
    {
        get => string.IsNullOrWhiteSpace(_detectedGameVersion) ? "Unknown" : _detectedGameVersion;
        private set
        {
            if (_detectedGameVersion == value)
            {
                return;
            }

            _detectedGameVersion = value;
            OnPropertyChanged(nameof(DetectedGameVersion));
            OnPropertyChanged(nameof(IsGameVersionOverridden));
            OnPropertyChanged(nameof(GameVersionHeaderText));
        }
    }

    public bool IsGameVersionOverridden =>
        !string.IsNullOrWhiteSpace(_settings.GameVersionOverride)
        && !string.Equals(_settings.GameVersionOverride, _detectedGameVersion, StringComparison.OrdinalIgnoreCase);

    public string GameVersionHeaderText
    {
        get
        {
            if (!IsGameVersionOverridden || string.IsNullOrWhiteSpace(_detectedGameVersion))
            {
                return $"Vintage Story {GameVersion}";
            }

            return $"Vintage Story {GameVersion} (detected {DetectedGameVersion})";
        }
    }

    public string SelectedGameVersionOption =>
        string.IsNullOrWhiteSpace(_settings.GameVersionOverride)
            ? AutoGameVersionLabel
            : _settings.GameVersionOverride;

    public string Summary
    {
        get
        {
            var updates = Mods.Count(mod => mod.CanUpdate);
            var modLabel = Mods.Count == 1 ? "mod" : "mods";
            var updateLabel = updates == 1 ? "update" : "updates";
            return $"{Mods.Count} {modLabel} installed, {updates} compatible {updateLabel} available";
        }
    }

    public bool IsAppUpdateAvailable
    {
        get => _isAppUpdateAvailable;
        private set
        {
            if (_isAppUpdateAvailable == value)
            {
                return;
            }

            _isAppUpdateAvailable = value;
            OnPropertyChanged(nameof(IsAppUpdateAvailable));
        }
    }

    public string AppUpdateBannerText
    {
        get => _appUpdateBannerText;
        private set
        {
            if (_appUpdateBannerText == value)
            {
                return;
            }

            _appUpdateBannerText = value;
            OnPropertyChanged(nameof(AppUpdateBannerText));
        }
    }

    public string OfficialAppModPageUrl
    {
        get => _officialAppModPageUrl;
        private set
        {
            if (_officialAppModPageUrl == value)
            {
                return;
            }

            _officialAppModPageUrl = value;
            OnPropertyChanged(nameof(OfficialAppModPageUrl));
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(InstallPath));
        OnPropertyChanged(nameof(ModsPath));
        OnPropertyChanged(nameof(SelectedGameVersionOption));
        await LoadGameVersionOptionsAsync(cancellationToken).ConfigureAwait(true);
        await ScanAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAppUpdateStatusAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task SaveAndScanAsync(CancellationToken cancellationToken = default)
    {
        await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(true);
        await ScanAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task SetGameVersionSelectionAsync(string? selection, CancellationToken cancellationToken = default)
    {
        var normalizedSelection = NormalizeInput(selection);
        var newOverride = string.Equals(normalizedSelection, AutoGameVersionLabel, StringComparison.Ordinal)
            ? null
            : normalizedSelection;

        if (string.Equals(_settings.GameVersionOverride, newOverride, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.GameVersionOverride = newOverride;
        OnPropertyChanged(nameof(SelectedGameVersionOption));
        ApplyEffectiveGameVersionToScanResult();
        GameVersion = _scanResult?.GameVersion ?? "";
        await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(true);

        StatusMessage = _scanResult?.GameVersion is null
            ? "Set the Vintage Story install path to enable game-compatible update checks."
            : IsGameVersionOverridden
                ? $"Using Vintage Story {_scanResult.GameVersion} for compatibility checks (detected {DetectedGameVersion})."
                : $"Using detected Vintage Story {_scanResult.GameVersion} for compatibility checks.";
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        await RunBusyAsync("Scanning installed mods...", async () =>
        {
            _scanResult = await _updaterService.ScanAsync(_settings, cancellationToken).ConfigureAwait(true);
            await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(true);

            DetectedGameVersion = _scanResult.DetectedGameVersion ?? "";
            GameVersion = _scanResult.GameVersion ?? "";
            EnsureGameVersionOptionsIncludeCurrentValues();
            InstallPath = _scanResult.Paths.InstallPath ?? "";
            ModsPath = _scanResult.Paths.ModsPath;

            Mods.Clear();
            foreach (var mod in _scanResult.Mods)
            {
                Mods.Add(new ModRowViewModel(mod));
            }

            Backups.Clear();
            foreach (var backup in _scanResult.Backups)
            {
                Backups.Add(new BackupRowViewModel(backup));
            }

            StatusMessage = _scanResult.GameVersion is null
                ? "Set the Vintage Story install path to enable game-compatible update checks."
                : IsGameVersionOverridden
                    ? $"Scan complete. Using Vintage Story {_scanResult.GameVersion} (detected {DetectedGameVersion})."
                    : $"Scan complete for Vintage Story {_scanResult.GameVersion}.";
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(SelectedGameVersionOption));
        }).ConfigureAwait(true);

        await ResolveModPageLinksAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task EnsureModPageLinkAsync(string modIdentifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modIdentifier))
        {
            return;
        }

        var existingRow = Mods.FirstOrDefault(mod =>
            mod.Identifier.Equals(modIdentifier, StringComparison.OrdinalIgnoreCase));
        if (existingRow?.ModPageUrl is not null)
        {
            return;
        }

        var existingBackup = Backups.FirstOrDefault(backup =>
            backup.Identifier.Equals(modIdentifier, StringComparison.OrdinalIgnoreCase));
        if (existingBackup?.ModPageUrl is not null)
        {
            return;
        }

        try
        {
            var reference = await _updaterService.TryResolveModReferenceAsync(modIdentifier, cancellationToken).ConfigureAwait(true);
            if (reference is null)
            {
                return;
            }

            ApplyModReferences(new Dictionary<string, ModDbModReference>(StringComparer.OrdinalIgnoreCase)
            {
                [modIdentifier] = reference
            });
        }
        catch
        {
            // Mod page links are best-effort.
        }
    }

    public async Task CheckUpdatesAsync(CancellationToken cancellationToken = default)
    {
        await RunBusyAsync("Checking official Vintage Story ModDB...", async () =>
        {
            if (_scanResult is null)
            {
                _scanResult = await _updaterService.ScanAsync(_settings, cancellationToken).ConfigureAwait(true);
            }

            var statuses = await _updaterService.CheckUpdatesAsync(_scanResult, cancellationToken).ConfigureAwait(true);
            foreach (var mod in Mods)
            {
                statuses.TryGetValue(mod.Identifier, out var status);
                mod.ApplyUpdateStatus(status);
            }

            var updates = Mods.Count(mod => mod.CanUpdate);
            StatusMessage = updates == 0
                ? "No compatible updates found."
                : $"{updates} compatible update(s) found.";
            OnPropertyChanged(nameof(Summary));
        }).ConfigureAwait(true);

        await ResolveModPageLinksAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAppUpdateStatusAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task UpdateModAsync(ModRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (_scanResult is null)
        {
            StatusMessage = "Scan installed mods before updating.";
            return;
        }

        if (row.UpdateStatus?.HasUpdate != true)
        {
            StatusMessage = $"{row.Name} does not have a downloadable compatible update.";
            return;
        }

        ModUpdateInstallResult? result = null;
        var succeeded = await RunBusyAsync($"Updating {row.Name}...", async () =>
        {
            result = await _updaterService.UpdateModAsync(
                row.Mod,
                row.UpdateStatus!,
                _scanResult.Paths.ModsPath,
                cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (!succeeded || result is null)
        {
            if (succeeded)
            {
                StatusMessage = $"{row.Name} update finished without a result. Check the update log in .vintage-mod-updater/logs.";
            }

            return;
        }

        await ScanAsync(cancellationToken).ConfigureAwait(true);
        await CheckUpdatesAsync(cancellationToken).ConfigureAwait(true);

        var installedVersion = result.InstalledVersion ?? row.UpdateStatus!.AvailableVersion ?? "unknown";
        StatusMessage =
            $"{row.Name} updated to {installedVersion}. Installed as {Path.GetFileName(result.DestinationPath)}. "
            + $"Backup saved. Log: {result.LogPath}";
    }

    public async Task UpdateAllAsync(CancellationToken cancellationToken = default)
    {
        var updateRows = Mods.Where(mod => mod.CanUpdate).ToArray();
        foreach (var row in updateRows)
        {
            await UpdateModAsync(row, cancellationToken).ConfigureAwait(true);
        }
    }

    public string BuildModSnapshotCsv()
    {
        var mods = Mods.Select(row => row.Mod);
        var entries = ModSnapshotCsv.CreateEntriesFromInstalledMods(mods, defaultUpdate: true);
        return ModSnapshotCsv.Build(entries);
    }

    public string SuggestModSnapshotFileName()
    {
        var gameVersion = string.IsNullOrWhiteSpace(_gameVersion) || _gameVersion == "Unknown"
            ? "unknown-game"
            : _gameVersion;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            gameVersion = gameVersion.Replace(invalid, '-');
        }

        return $"mod-snapshot-{gameVersion}-{DateTime.Now:yyyy-MM-dd}.csv";
    }

    public async Task ExportModSnapshotToFileAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await File.WriteAllTextAsync(path, BuildModSnapshotCsv(), cancellationToken).ConfigureAwait(true);
            StatusMessage = $"Mod snapshot exported to {path}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export mod snapshot: {ex.Message}";
        }
    }

    public async Task RestoreBackupAsync(BackupRowViewModel row, CancellationToken cancellationToken = default)
    {
        await RunBusyAsync($"Restoring {row.Name}...", async () =>
        {
            await _updaterService.RestoreBackupAsync(row.Backup, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"{row.Name} {row.Version} was restored.";
        }).ConfigureAwait(true);

        await ScanAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task ResolveModPageLinksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var identifiers = Mods
                .Select(mod => mod.Identifier)
                .Concat(Backups.Select(backup => backup.Identifier))
                .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (identifiers.Length == 0)
            {
                return;
            }

            var references = await _updaterService.ResolveModReferencesAsync(identifiers, cancellationToken).ConfigureAwait(true);
            ApplyModReferences(references);
        }
        catch
        {
            // Mod page links are best-effort.
        }
    }

    private void ApplyModReferences(IReadOnlyDictionary<string, ModDbModReference> references)
    {
        foreach (var mod in Mods)
        {
            if (references.TryGetValue(mod.Identifier, out var reference))
            {
                mod.SetModDbReference(reference);
            }
        }

        foreach (var backup in Backups)
        {
            if (references.TryGetValue(backup.Identifier, out var reference))
            {
                backup.SetModDbAssetId(reference.AssetId);
            }
        }
    }

    private async Task LoadGameVersionOptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var versions = await _updaterService.GetGameVersionsAsync(cancellationToken).ConfigureAwait(true);
            _gameVersionOptions = BuildGameVersionOptions(versions);
        }
        catch
        {
            _gameVersionOptions = BuildGameVersionOptions(Array.Empty<string>());
        }

        OnPropertyChanged(nameof(GameVersionOptions));
    }

    private void EnsureGameVersionOptionsIncludeCurrentValues()
    {
        var requiredVersions = new List<string>();
        if (!string.IsNullOrWhiteSpace(_detectedGameVersion))
        {
            requiredVersions.Add(_detectedGameVersion);
        }

        if (!string.IsNullOrWhiteSpace(_settings.GameVersionOverride))
        {
            requiredVersions.Add(_settings.GameVersionOverride);
        }

        var merged = _gameVersionOptions
            .Where(option => !string.Equals(option, AutoGameVersionLabel, StringComparison.Ordinal))
            .Concat(requiredVersions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(version => version, Comparer<string>.Create(VersionComparer.Compare))
            .ToArray();

        var rebuilt = BuildGameVersionOptions(merged);
        if (rebuilt.SequenceEqual(_gameVersionOptions, StringComparer.Ordinal))
        {
            return;
        }

        _gameVersionOptions = rebuilt;
        OnPropertyChanged(nameof(GameVersionOptions));
    }

    private static IReadOnlyList<string> BuildGameVersionOptions(IEnumerable<string> versions)
    {
        var options = new List<string> { AutoGameVersionLabel };
        options.AddRange(versions);
        return options;
    }

    private void ApplyEffectiveGameVersionToScanResult()
    {
        if (_scanResult is null)
        {
            return;
        }

        var effectiveVersion = VintageModUpdaterService.ResolveGameVersion(
            _scanResult.DetectedGameVersion,
            _settings.GameVersionOverride);
        _scanResult = _scanResult with { GameVersion = effectiveVersion };
    }

    private async Task<bool> RunBusyAsync(string busyMessage, Func<Task> action)
    {
        IsBusy = true;
        StatusMessage = busyMessage;

        try
        {
            await action().ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            var logHint = ex.Data["LogPath"] is string logPath && !string.IsNullOrWhiteSpace(logPath)
                ? $" Details were written to {logPath}."
                : string.Empty;
            StatusMessage = $"{ex.Message}{logHint}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? NormalizeInput(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task RefreshAppUpdateStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var currentVersion = ReadCurrentAppVersion();
            var appUpdate = await _updaterService.CheckAppUpdateAsync(currentVersion, cancellationToken).ConfigureAwait(true);
            OfficialAppModPageUrl = appUpdate.ModPageUrl;

            if (appUpdate.UpdateAvailable && !string.IsNullOrWhiteSpace(appUpdate.LatestVersion))
            {
                IsAppUpdateAvailable = true;
                AppUpdateBannerText =
                    $"A new Vintage Mod Updater release ({appUpdate.LatestVersion}) is available. "
                    + $"You are running {appUpdate.CurrentVersion}.";
                return;
            }
        }
        catch
        {
            // Keep update checks non-blocking. Failures should not disrupt mod management.
        }

        IsAppUpdateAvailable = false;
        AppUpdateBannerText = "";
    }

    private static string ReadCurrentAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var rawVersion = NormalizeInput(informationalVersion) ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
        var separatorIndex = rawVersion.IndexOfAny(new[] { '+', '-', ' ' });
        if (separatorIndex > 0)
        {
            rawVersion = rawVersion[..separatorIndex];
        }

        return Version.TryParse(rawVersion, out _) ? rawVersion : "0.0.0";
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
