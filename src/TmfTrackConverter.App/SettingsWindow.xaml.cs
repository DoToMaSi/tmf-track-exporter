using System.IO;
using System.Windows;
using Microsoft.Win32;
using TmfTrackConverter.Core;

namespace TmfTrackConverter.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string? _originalFolder;

    public bool RebuildRequested { get; private set; }

    public bool GameFolderChanged { get; private set; }

    public SettingsWindow(AppSettings settings, GameInstall? install)
    {
        InitializeComponent();
        ThemeHelper.Apply(this);
        _settings = settings;
        _originalFolder = settings.GameFolder;
        GameFolderBox.Text = settings.GameFolder ?? "";
        TracksFolderBox.Text = settings.DefaultTracksFolder ?? install?.DefaultChallengesFolder ?? "";
        IncludeEnvironment.IsChecked = settings.IncludeEnvironmentMesh;
        SkipClips.IsChecked = settings.SkipClipBlocks;
        ExportObj.IsChecked = settings.ExportObjSidecar;
        UpdateCacheLabel(install);
    }

    private void UpdateCacheLabel(GameInstall? install)
    {
        if (install is null)
        {
            CacheStatus.Text = "No valid game folder.";
            return;
        }

        var ready = CacheManifest.IsReady(install);
        var size = Directory.Exists(AppPaths.CacheRoot)
            ? new GameAssetStore().CacheSizeBytes()
            : 0;
        if (ready && size == 0)
        {
            var store = GameAssetStore.Open(install);
            size = store.CacheSizeBytes();
        }

        CacheStatus.Text = ready
            ? $"Cache is ready ({FormatSize(size)}). Path: {AppPaths.CacheRoot}"
            : "Cache is missing or out of date. Save and the app will decrypt again, or click Rebuild cache.";
    }

    private void OnBrowseGame(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select TmForever.exe",
            Filter = "TmForever.exe|TmForever.exe"
        };

        if (dialog.ShowDialog(this) == true)
        {
            GameFolderBox.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
        }
    }

    private void OnBrowseTracks(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Pick any file inside the tracks folder",
            Filter = "Challenge (*.Challenge.Gbx)|*.Challenge.Gbx|All files (*.*)|*.*",
            CheckFileExists = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            TracksFolderBox.Text = Path.GetDirectoryName(dialog.FileName) ?? TracksFolderBox.Text;
        }
    }

    private void OnRebuild(object sender, RoutedEventArgs e)
    {
        ApplyToSettings();
        RebuildRequested = true;
        DialogResult = true;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ApplyToSettings();
        GameFolderChanged = !string.Equals(_originalFolder, _settings.GameFolder, StringComparison.OrdinalIgnoreCase);
        DialogResult = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ApplyToSettings()
    {
        _settings.GameFolder = string.IsNullOrWhiteSpace(GameFolderBox.Text) ? null : GameFolderBox.Text;
        _settings.DefaultTracksFolder = string.IsNullOrWhiteSpace(TracksFolderBox.Text) ? null : TracksFolderBox.Text;
        _settings.IncludeEnvironmentMesh = IncludeEnvironment.IsChecked == true;
        _settings.SkipClipBlocks = SkipClips.IsChecked == true;
        _settings.ExportObjSidecar = ExportObj.IsChecked == true;
        SettingsStore.Save(_settings);
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
}
