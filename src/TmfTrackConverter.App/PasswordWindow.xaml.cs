using System.Windows;

namespace TmfTrackConverter.App;

public partial class PasswordWindow : Window
{
    public string? Password { get; private set; }

    public PasswordWindow()
    {
        InitializeComponent();
        ThemeHelper.Apply(this);
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Password;
        DialogResult = true;
        Close();
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        Password = null;
        DialogResult = false;
        Close();
    }
}
