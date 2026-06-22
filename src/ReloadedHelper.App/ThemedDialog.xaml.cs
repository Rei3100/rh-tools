using System.Windows;

namespace ReloadedHelper.App;

public partial class ThemedDialog : Window
{
    private bool _result;

    private ThemedDialog() { InitializeComponent(); }

    public static bool Show(Window? owner, string title, string message,
        string okText = "OK", string? cancelText = null)
    {
        var dlg = new ThemedDialog();
        if (owner is not null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkButton.Content = okText;
        if (cancelText is null) dlg.CancelButton.Visibility = Visibility.Collapsed;
        else dlg.CancelButton.Content = cancelText;
        dlg.ShowDialog();
        return dlg._result;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) { _result = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { _result = false; Close(); }
}
