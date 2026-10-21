using System.IO;
using System.Windows;
using Microsoft.Win32;
using TmfTrackConverter.Core;
using TmfTrackConverter.Export;

namespace TmfTrackConverter.App;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private GameInstall? _install;
    private GameAssetStore? _store;
    private TrackScene? _scene;
    private LoadedMap? _map;

    public MainWindow()
    {
        InitializeComponent();
        ThemeHelper.Apply(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        IncludeEnvironmentExport.IsChecked = _settings.IncludeEnvironmentMesh;
        await EnsureInstallReadyAsync();
        RefreshInfo();
    }

    private async Task EnsureInstallReadyAsync(bool forceRebuild = false)
    {
        if (_settings.GameFolder is null || !GameInstall.TryDetect(_settings.GameFolder, out var install, out var error))
        {
            if (!PickGameFolder())
            {
                Log("No game folder selected. Open Settings to choose one.");
                return;
            }

            if (!GameInstall.TryDetect(_settings.GameFolder, out install, out error))
            {
                MessageBox.Show(this, error, "Invalid game folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _install = install;
        _settings.DefaultTracksFolder ??= install!.DefaultChallengesFolder;
        SettingsStore.Save(_settings);

        if (forceRebuild || !CacheManifest.IsReady(install!))
        {
            if (!await RunDecryptAsync(install!))
            {
                Log("Cache is not ready. Conversion stays disabled until decrypt finishes.");
                _store = null;
                _settings.CacheReady = false;
                SettingsStore.Save(_settings);
                RefreshInfo();
                return;
            }
        }

        _settings.CacheReady = true;
        SettingsStore.Save(_settings);
        _store = GameAssetStore.Open(install!);
        Log($"Game data ready ({install!.Edition}, {install.AvailableEnvironments.Count} environments).");
        RefreshInfo();
    }

    private bool PickGameFolder()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select TmForever.exe in the game folder",
            Filter = "TmForever.exe|TmForever.exe|Executable (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        _settings.GameFolder = Path.GetDirectoryName(dialog.FileName);
        SettingsStore.Save(_settings);
        return true;
    }

    private async Task<bool> RunDecryptAsync(GameInstall install)
    {
        var window = new DecryptWindow { Owner = this };
        var result = await window.RunAsync(install);
        if (result)
        {
            Log("Decrypt finished. Cache is ready.");
        }
        else
        {
            Log("Decrypt cancelled or failed.");
        }

        return result;
    }

    private async void OnOpenTrack(object sender, RoutedEventArgs e)
    {
        if (_install is null || _store is null)
        {
            await EnsureInstallReadyAsync();
            if (_store is null)
            {
                return;
            }
        }

        var dialog = new OpenFileDialog
        {
            Title = "Open Challenge.Gbx",
            Filter = "TrackMania Forever tracks (*.Challenge.Gbx)|*.Challenge.Gbx|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(_settings.DefaultTracksFolder)
                ? _settings.DefaultTracksFolder
                : _install!.DefaultChallengesFolder
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var reader = new MapReader();
            var map = reader.Load(dialog.FileName, _install!);
            if (map.HasPassword)
            {
                var password = new PasswordWindow { Owner = this };
                password.ShowDialog();
            }

            StatusText.Text = "Building scene…";
            var builder = new TrackBuilder(_store!, _settings);
            var progress = new Progress<TrackBuildProgress>(p =>
            {
                StatusText.Text = p.Status;
            });

            var scene = await Task.Run(() => builder.Build(map, progress));
            _map = map;
            _scene = scene;
            Schematic.Scene = scene;
            PreviewHint.Visibility = Visibility.Collapsed;
            ExportButton.IsEnabled = scene.Instances.Count > 0;
            RefreshInfo();
            Log($"Loaded {map.MapName} — {scene.Instances.Count} instances, {scene.Meshes.Count} meshes.");
            foreach (var warning in scene.Warnings)
            {
                Log(warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open track", MessageBoxButton.OK, MessageBoxImage.Error);
            Log(ex.Message);
        }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_scene is null || _store is null || _map is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export FBX",
            Filter = "FBX (*.fbx)|*.fbx",
            FileName = SanitizeFileName(_map.MapName) + ".fbx"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetBusy(true);
        StatusText.Text = "Exporting…";
        var window = new ExportWindow { Owner = this };
        var result = await window.RunAsync(_scene, _store, new ExportOptions
        {
            FbxPath = dialog.FileName,
            WriteObjSidecar = _settings.ExportObjSidecar,
            IncludeEnvironment = IncludeEnvironmentExport.IsChecked == true
        });
        SetBusy(false);

        if (window.Cancelled)
        {
            Log("Export cancelled.");
            StatusText.Text = "Export cancelled.";
            return;
        }

        if (result is null)
        {
            StatusText.Text = "Export failed.";
            return;
        }

        var message = $"Exported {result.FbxPath} ({result.TextureCount} textures).";
        if (result.ObjPath is not null)
        {
            message += $" OBJ sidecar: {result.ObjPath}";
        }

        Log(message);
        if (_scene.MissingTextures.Count > 0)
        {
            Log($"Missing textures ({_scene.MissingTextures.Count}): {string.Join(", ", _scene.MissingTextures.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))}");
        }

        StatusText.Text = "Export finished.";
        MessageBox.Show(this, message, "Export finished", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SetBusy(bool busy)
    {
        OpenButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        IncludeEnvironmentExport.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && _scene is { Instances.Count: > 0 } && _settings.CacheReady;
    }

    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings, _install) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _settings = SettingsStore.Load();
            IncludeEnvironmentExport.IsChecked = _settings.IncludeEnvironmentMesh;
            if (window.RebuildRequested || window.GameFolderChanged)
            {
                await EnsureInstallReadyAsync(forceRebuild: window.RebuildRequested);
            }
            else if (_install is not null)
            {
                _store = GameAssetStore.Open(_install);
            }

            RefreshInfo();
        }
    }

    private void RefreshInfo()
    {
        if (_map is null)
        {
            InfoName.Text = "No track loaded";
            InfoAuthor.Text = "";
            InfoEnvironment.Text = "";
            InfoDecoration.Text = "";
            InfoSize.Text = "";
            InfoBlocks.Text = "";
            InfoPassword.Text = "";
        }
        else
        {
            InfoName.Text = _map.MapName;
            InfoAuthor.Text = string.IsNullOrWhiteSpace(_map.Author) ? "Unknown author" : _map.Author;
            InfoEnvironment.Text = $"Environment: {_map.Environment.DisplayName()}";
            InfoDecoration.Text = $"Decoration: {_map.DecorationId}";
            InfoSize.Text = $"Size: {_map.Size.X} × {_map.Size.Y} × {_map.Size.Z}  (block { _map.BlockSize.X }×{_map.BlockSize.Y}×{_map.BlockSize.Z})";
            InfoBlocks.Text = _scene is null
                ? $"Blocks: {_map.BlockCount}"
                : $"Blocks: {_map.BlockCount}  ·  exported instances: {_scene.Instances.Count}";
            InfoPassword.Text = _map.HasPassword ? "Editor password: yes (not required to export)" : "Editor password: no";
        }

        if (_install is null)
        {
            InfoEdition.Text = "Game folder: not set";
            InfoCache.Text = "";
            return;
        }

        InfoEdition.Text = $"Install: {_install.Edition}  ·  {_install.AvailableEnvironments.Count} environments";
        var size = _store?.CacheSizeBytes() ?? 0;
        InfoCache.Text = _settings.CacheReady
            ? $"Cache ready ({FormatSize(size)}) at {AppPaths.CacheRoot}"
            : "Cache not ready — decrypt the game packs first.";
        StatusText.Text = _settings.CacheReady ? "Ready." : "Waiting for cache.";
        ExportButton.IsEnabled = _scene is { Instances.Count: > 0 } && _settings.CacheReady;
    }

    private void Log(string message)
    {
        LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
