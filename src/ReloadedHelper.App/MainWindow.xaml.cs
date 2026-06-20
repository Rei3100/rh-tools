using System.Windows;
using ReloadedHelper.Core;

namespace ReloadedHelper.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
