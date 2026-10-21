using System.Windows;
using TmfTrackConverter.Core;
using TmfTrackConverter.Export;

namespace TmfTrackConverter.App;

public partial class ExportWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _completed;

    public ExportResult? Result { get; private set; }

    public bool Cancelled { get; private set; }

    public ExportWindow()
    {
        InitializeComponent();
        ThemeHelper.Apply(this);
        Closing += (_, e) =>
        {
            if (!_completed)
            {
                _cts.Cancel();
                e.Cancel = true;
            }
        };
    }

    public async Task<ExportResult?> RunAsync(TrackScene scene, GameAssetStore store, ExportOptions options)
    {
        IncludeEnvironmentBox.IsChecked = options.IncludeEnvironment;
        Show();
        var service = new ExportService();
        var progress = new Progress<ExportProgress>(p =>
        {
            StatusLabel.Text = p.Status;
            Bar.Value = p.Fraction;
            FileLabel.Text = string.IsNullOrWhiteSpace(p.Detail)
                ? $"{p.Current} / {p.Total}"
                : $"{p.Detail}  ({p.Current} / {p.Total})";
        });

        try
        {
            Result = await Task.Run(() => service.Export(scene, store, options, progress, _cts.Token), _cts.Token);
            _completed = true;
            Close();
            return Result;
        }
        catch (OperationCanceledException)
        {
            Cancelled = true;
            _completed = true;
            Close();
            return null;
        }
        catch (Exception ex)
        {
            _completed = true;
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return null;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        StatusLabel.Text = "Cancelling…";
    }
}
