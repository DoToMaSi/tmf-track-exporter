using System.Windows;
using TmfTrackConverter.Core;

namespace TmfTrackConverter.App;

public partial class DecryptWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _completed;

    public DecryptWindow()
    {
        InitializeComponent();
        ThemeHelper.Apply(this);
        Closing += (_, e) =>
        {
            if (!_completed)
            {
                _cts.Cancel();
            }
        };
    }

    public async Task<bool> RunAsync(GameInstall install)
    {
        Show();
        var decryptor = new PakDecryptor();
        var progress = new Progress<DecryptProgress>(p =>
        {
            StatusLabel.Text = p.Status;
            Bar.Value = p.Fraction;
            FileLabel.Text = p.CurrentFile ?? "";
        });

        try
        {
            await decryptor.DecryptAsync(install, progress, _cts.Token);
            _completed = true;
            Close();
            return true;
        }
        catch (OperationCanceledException)
        {
            _completed = true;
            Close();
            return false;
        }
        catch (Exception ex)
        {
            _completed = true;
            MessageBox.Show(this, ex.Message, "Decrypt failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return false;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        StatusLabel.Text = "Cancelling…";
    }
}
