using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Diagnostics;
using VintageModUpdater.App.ViewModels;

namespace VintageModUpdater.App.Views;

public sealed class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly TextBox _installPathBox = new();
    private readonly TextBox _modsPathBox = new();
    private readonly TextBlock _gameVersionText = new();
    private readonly TextBlock _summaryText = new();
    private readonly Border _appUpdateBanner = new();
    private readonly TextBlock _appUpdateBannerText = new();
    private readonly Button _viewAppUpdateButton = new();
    private readonly TextBlock _statusText = new();
    private readonly StackPanel _modsPanel = new();
    private readonly StackPanel _backupsPanel = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _scanButton = new();
    private readonly Button _checkButton = new();
    private readonly Button _updateAllButton = new();

    private static readonly IBrush PageBrush = new SolidColorBrush(Color.Parse("#f6f3ed"));
    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.Parse("#fffdf8"));
    private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#1f2428"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#5d6972"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#0f766e"));
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#d7d0c5"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#a15c07"));
    private static readonly IBrush BannerBrush = new SolidColorBrush(Color.Parse("#fff4cc"));

    public MainWindow()
    {
        Title = "Vintage Mod Updater";
        MinWidth = 980;
        MinHeight = 680;
        Width = 1180;
        Height = 760;
        Background = PageBrush;
        Content = BuildContent();
        _viewModel.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(SyncChrome);

        Opened += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            SyncFromViewModel();
        };
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(24),
            RowSpacing = 16
        };

        var header = BuildHeader();
        root.Children.Add(header);
        Grid.SetRow(header, 0);

        var paths = BuildPathsPanel();
        root.Children.Add(paths);
        Grid.SetRow(paths, 2);

        var appUpdateBanner = BuildAppUpdateBanner();
        root.Children.Add(appUpdateBanner);
        Grid.SetRow(appUpdateBanner, 1);

        var tabs = BuildMainTabs();
        root.Children.Add(tabs);
        Grid.SetRow(tabs, 3);

        var status = BuildStatusBar();
        root.Children.Add(status);
        Grid.SetRow(status, 4);

        return root;
    }

    private Control BuildAppUpdateBanner()
    {
        _appUpdateBannerText.Foreground = WarningBrush;
        _appUpdateBannerText.TextWrapping = TextWrapping.Wrap;
        _appUpdateBannerText.VerticalAlignment = VerticalAlignment.Center;

        _viewAppUpdateButton.Content = "View on ModDB";
        _viewAppUpdateButton.MinWidth = 130;
        _viewAppUpdateButton.Click += async (_, _) => await OpenAppUpdatePageAsync();

        _appUpdateBanner.Background = BannerBrush;
        _appUpdateBanner.BorderBrush = LineBrush;
        _appUpdateBanner.BorderThickness = new Thickness(1);
        _appUpdateBanner.CornerRadius = new CornerRadius(8);
        _appUpdateBanner.Padding = new Thickness(12, 10);
        _appUpdateBanner.IsVisible = false;
        _appUpdateBanner.Child = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                _appUpdateBannerText,
                _viewAppUpdateButton
            }
        };
        Grid.SetColumn(_viewAppUpdateButton, 1);

        return _appUpdateBanner;
    }

    private Control BuildHeader()
    {
        _gameVersionText.Foreground = MutedBrush;
        _gameVersionText.FontSize = 14;
        _summaryText.Foreground = InkBrush;
        _summaryText.FontSize = 18;
        _summaryText.FontWeight = FontWeight.SemiBold;

        _scanButton.Content = "Scan";
        _scanButton.Click += async (_, _) =>
        {
            PullPathInputs();
            await _viewModel.SaveAndScanAsync();
            SyncFromViewModel();
        };

        _checkButton.Content = "Check Updates";
        _checkButton.Click += async (_, _) =>
        {
            PullPathInputs();
            await _viewModel.SaveAndScanAsync();
            await _viewModel.CheckUpdatesAsync();
            SyncFromViewModel();
        };

        _updateAllButton.Content = "Update All";
        _updateAllButton.Click += async (_, _) =>
        {
            await _viewModel.UpdateAllAsync();
            SyncFromViewModel();
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                _scanButton,
                AccentButton(_checkButton),
                AccentButton(_updateAllButton)
            }
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16,
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Vintage Mod Updater",
                            FontSize = 28,
                            FontWeight = FontWeight.Bold,
                            Foreground = InkBrush
                        },
                        _gameVersionText,
                        _summaryText
                    }
                },
                actions
            }
        };

        Grid.SetColumn(actions, 1);
        return header;
    }

    private Control BuildPathsPanel()
    {
        _installPathBox.Watermark = "Vintage Story install path";
        _modsPathBox.Watermark = "Mods folder";

        var panel = new Border
        {
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                RowSpacing = 10,
                ColumnSpacing = 10,
                Children =
                {
                    PathLabel("Game Installation Directory"),
                    _installPathBox,
                    BrowseButton("Browse", BrowseInstallPathAsync),
                    PathLabel("Mods Directory"),
                    _modsPathBox,
                    BrowseButton("Browse", BrowseModsPathAsync)
                }
            }
        };

        var grid = (Grid)panel.Child!;
        Grid.SetColumn(_installPathBox, 1);
        Grid.SetColumn(grid.Children[2], 2);
        Grid.SetRow(grid.Children[3], 1);
        Grid.SetRow(_modsPathBox, 1);
        Grid.SetColumn(_modsPathBox, 1);
        Grid.SetRow(grid.Children[5], 1);
        Grid.SetColumn(grid.Children[5], 2);

        return panel;
    }

    private Control BuildMainTabs()
    {
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem
                {
                    Header = "Installed Mods",
                    Content = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = _modsPanel
                    }
                },
                new TabItem
                {
                    Header = "Backups",
                    Content = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = _backupsPanel
                    }
                }
            }
        };

        return tabs;
    }

    private Control BuildStatusBar()
    {
        _progress.IsIndeterminate = true;
        _progress.Height = 4;
        _progress.IsVisible = false;
        _statusText.Foreground = MutedBrush;
        _statusText.TextWrapping = TextWrapping.Wrap;

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _progress,
                _statusText
            }
        };
    }

    private void SyncFromViewModel()
    {
        _installPathBox.Text = _viewModel.InstallPath;
        _modsPathBox.Text = _viewModel.ModsPath;
        SyncChrome();

        RenderMods();
        RenderBackups();
    }

    private void SyncChrome()
    {
        _gameVersionText.Text = $"Vintage Story {_viewModel.GameVersion}";
        _summaryText.Text = _viewModel.Summary;
        _appUpdateBanner.IsVisible = _viewModel.IsAppUpdateAvailable;
        _appUpdateBannerText.Text = _viewModel.AppUpdateBannerText;
        _statusText.Text = _viewModel.StatusMessage;
        _progress.IsVisible = _viewModel.IsBusy;

        _scanButton.IsEnabled = !_viewModel.IsBusy;
        _checkButton.IsEnabled = !_viewModel.IsBusy;
        _updateAllButton.IsEnabled = !_viewModel.IsBusy && _viewModel.Mods.Any(mod => mod.CanUpdate);
        _viewAppUpdateButton.IsEnabled = !_viewModel.IsBusy && _viewModel.IsAppUpdateAvailable;
    }

    private void PullPathInputs()
    {
        _viewModel.InstallPath = _installPathBox.Text ?? "";
        _viewModel.ModsPath = _modsPathBox.Text ?? "";
    }

    private void RenderMods()
    {
        _modsPanel.Children.Clear();
        _modsPanel.Spacing = 10;

        if (_viewModel.Mods.Count == 0)
        {
            _modsPanel.Children.Add(EmptyState("No installed mods found."));
            return;
        }

        _modsPanel.Children.Add(HeaderRow("Mod", "Installed", "Status", "Source", "Action"));
        foreach (var row in _viewModel.Mods)
        {
            _modsPanel.Children.Add(ModRow(row));
        }
    }

    private void RenderBackups()
    {
        _backupsPanel.Children.Clear();
        _backupsPanel.Spacing = 10;

        if (_viewModel.Backups.Count == 0)
        {
            _backupsPanel.Children.Add(EmptyState("No backups yet."));
            return;
        }

        _backupsPanel.Children.Add(HeaderRow("Mod", "Version", "Created", "Source", "Action"));
        foreach (var row in _viewModel.Backups)
        {
            _backupsPanel.Children.Add(BackupRow(row));
        }
    }

    private Control ModRow(ModRowViewModel row)
    {
        var updateButton = new Button
        {
            Content = row.UpdateButtonText,
            IsEnabled = row.CanUpdate && !_viewModel.IsBusy,
            MinWidth = 126
        };
        updateButton.Click += async (_, _) =>
        {
            await _viewModel.UpdateModAsync(row);
            SyncFromViewModel();
        };

        return DataRow(
            Cell(StackText(row.Name, row.Identifier)),
            Cell(row.Version),
            Cell(row.StatusText, row.CanUpdate ? WarningBrush : MutedBrush),
            Cell(row.FileName),
            updateButton);
    }

    private Control BackupRow(BackupRowViewModel row)
    {
        var restoreButton = new Button
        {
            Content = "Restore",
            IsEnabled = !_viewModel.IsBusy,
            MinWidth = 126
        };
        restoreButton.Click += async (_, _) =>
        {
            await _viewModel.RestoreBackupAsync(row);
            SyncFromViewModel();
        };

        return DataRow(
            Cell(StackText(row.Name, row.Identifier)),
            Cell(row.Version),
            Cell(row.CreatedAt),
            Cell(row.SourceFile),
            restoreButton);
    }

    private static Control HeaderRow(params string[] labels)
    {
        var grid = RowGrid();
        for (var i = 0; i < labels.Length; i++)
        {
            var text = new TextBlock
            {
                Text = labels[i],
                FontWeight = FontWeight.SemiBold,
                Foreground = MutedBrush,
                Margin = new Thickness(12, 0)
            };
            grid.Children.Add(text);
            Grid.SetColumn(text, i);
        }

        return grid;
    }

    private static Control DataRow(params Control[] cells)
    {
        var grid = RowGrid();
        for (var i = 0; i < cells.Length; i++)
        {
            grid.Children.Add(cells[i]);
            Grid.SetColumn(cells[i], i);
        }

        return new Border
        {
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = grid
        };
    }

    private static Grid RowGrid()
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2.1*,.8*,1.4*,1.4*,Auto"),
            ColumnSpacing = 12,
            MinHeight = 42
        };
    }

    private static Control Cell(string text, IBrush? brush = null)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = brush ?? InkBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Control Cell(Control control)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    private static Control StackText(string primary, string secondary)
    {
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = primary,
                    Foreground = InkBrush,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = secondary,
                    Foreground = MutedBrush,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private static Control EmptyState(string text)
    {
        return new Border
        {
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Child = new TextBlock
            {
                Text = text,
                Foreground = MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static TextBlock PathLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Width = 190,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static Button BrowseButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 92
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private async Task BrowseInstallPathAsync()
    {
        var folder = await PickFolderAsync("Vintage Story install path");
        if (folder is null)
        {
            return;
        }

        _installPathBox.Text = folder;
        PullPathInputs();
        await _viewModel.SaveAndScanAsync();
        SyncFromViewModel();
    }

    private async Task BrowseModsPathAsync()
    {
        var folder = await PickFolderAsync("Vintage Story mods folder");
        if (folder is null)
        {
            return;
        }

        _modsPathBox.Text = folder;
        PullPathInputs();
        await _viewModel.SaveAndScanAsync();
        SyncFromViewModel();
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider.CanPickFolder != true)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private static Button AccentButton(Button button)
    {
        button.Background = AccentBrush;
        button.Foreground = Brushes.White;
        return button;
    }

    private Task OpenAppUpdatePageAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.OfficialAppModPageUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening the browser is best-effort and should not interrupt app usage.
        }

        return Task.CompletedTask;
    }
}
