// src/ReloadedHelper.App/Views/HelpView.xaml.cs
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace ReloadedHelper.App.Views;

public partial class HelpView : UserControl
{
    public HelpView() => InitializeComponent();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
