// src/ReloadedHelper.App/ShellViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReloadedHelper.Core;

namespace ReloadedHelper.App;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    public MainViewModel   ModListVm  { get; }
    public SettingsViewModel SettingsVm { get; }
    public HelpViewModel   HelpVm     { get; } = new();

    private object _currentView;
    public object CurrentView
    {
        get => _currentView;
        set
        {
            if (_currentView == value) return;
            _currentView = value;
            Notify();
            Notify(nameof(IsModListActive));
            Notify(nameof(IsSettingsActive));
            Notify(nameof(IsHelpActive));
            Notify(nameof(IsOverlayVisible));
            Notify(nameof(CurrentOverlayView));
        }
    }

    public bool IsModListActive  => CurrentView == ModListVm;
    public bool IsSettingsActive => CurrentView == SettingsVm;
    public bool IsHelpActive     => CurrentView == HelpVm;

    public bool IsOverlayVisible => IsSettingsActive || IsHelpActive;

    public object? CurrentOverlayView =>
        IsSettingsActive ? SettingsVm :
        IsHelpActive ? (object?)HelpVm :
        null;

    public ShellViewModel(MainViewModel modListVm, SettingsViewModel settingsVm)
    {
        ModListVm  = modListVm;
        SettingsVm = settingsVm;
        _currentView = modListVm;
    }

    public void ShowModList()  => CurrentView = ModListVm;
    public void ShowSettings() => CurrentView = SettingsVm;
    public void ShowHelp()     => CurrentView = HelpVm;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
