using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace ReloadedHelper.App.Views;

public partial class ModListView : UserControl
{
    public ModListView() => InitializeComponent();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
