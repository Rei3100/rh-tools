using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rh_test_{Guid.NewGuid()}.json");

    [Fact]
    public void UiZoomPercent_DefaultIs100()
    {
        var vm = new SettingsViewModel(_path);
        Assert.Equal(100, vm.UiZoomPercent);
    }

    [Fact]
    public void Scale_ReflectsZoomPercent()
    {
        var vm = new SettingsViewModel(_path) { UiZoomPercent = 150 };
        Assert.Equal(1.5, vm.Scale);
    }

    [Fact]
    public void UiZoomPercent_ClampsTo80_200()
    {
        var vm = new SettingsViewModel(_path);
        vm.UiZoomPercent = 50;
        Assert.Equal(80, vm.UiZoomPercent);
        vm.UiZoomPercent = 300;
        Assert.Equal(200, vm.UiZoomPercent);
    }

    [Fact]
    public void Settings_RoundTrips_ToFile()
    {
        var vm1 = new SettingsViewModel(_path) { UiZoomPercent = 125, MinimizeToTray = false };
        var vm2 = new SettingsViewModel(_path);
        Assert.Equal(125, vm2.UiZoomPercent);
        Assert.False(vm2.MinimizeToTray);
    }

    [Fact]
    public void PropertyChanged_FiresOnZoomChange()
    {
        var vm = new SettingsViewModel(_path);
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);
        vm.UiZoomPercent = 120;
        Assert.Contains(nameof(SettingsViewModel.UiZoomPercent), fired);
        Assert.Contains(nameof(SettingsViewModel.Scale), fired);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
