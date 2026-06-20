using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReloadedHelper.Core;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly string _path;
    private AppSettings _s;

    public SettingsViewModel(string path)
    {
        _path = path;
        _s = SettingsStore.Load(path);
    }

    public int UiZoomPercent
    {
        get => _s.UiZoomPercent;
        set
        {
            var v = Math.Clamp(value, 80, 200);
            if (_s.UiZoomPercent == v) return;
            _s.UiZoomPercent = v;
            Notify();
            Notify(nameof(Scale));
            Save();
        }
    }

    public double Scale => UiZoomPercent / 100.0;

    public bool MinimizeToTray
    {
        get => _s.MinimizeToTray;
        set { if (_s.MinimizeToTray == value) return; _s.MinimizeToTray = value; Notify(); Save(); }
    }

    public bool RememberLastGame
    {
        get => _s.RememberLastGame;
        set { if (_s.RememberLastGame == value) return; _s.RememberLastGame = value; Notify(); Save(); }
    }

    public string? LastGameId
    {
        get => _s.LastGameId;
        set { if (_s.LastGameId == value) return; _s.LastGameId = value; Notify(); Save(); }
    }

    public string? ReloadedInstallPath
    {
        get => _s.ReloadedInstallPath;
        set { if (_s.ReloadedInstallPath == value) return; _s.ReloadedInstallPath = value; Notify(); Save(); }
    }

    private void Save() => SettingsStore.Save(_path, _s);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
