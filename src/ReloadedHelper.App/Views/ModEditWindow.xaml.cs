// src/ReloadedHelper.App/Views/ModEditWindow.xaml.cs
using System.IO;
using System.Windows;
using ReloadedHelper.Core;

namespace ReloadedHelper.App.Views;

public partial class ModEditWindow : Window
{
    private readonly ModLoadEntry _entry;
    private readonly MainViewModel _vm;

    public ModEditWindow(ModLoadEntry entry, MainViewModel vm)
    {
        _entry = entry;
        _vm = vm;
        InitializeComponent();
        LoadFields();
    }

    private void LoadFields()
    {
        var ud = UserDataStore.Load(UserDataStore.DefaultPath);
        ud.Mods.TryGetValue(_entry.ModId, out var data);

        TbName.Text = data?.TranslatedName ?? _entry.Info?.ModName ?? "";
        TbDescription.Text = data?.TranslatedDescription ?? _entry.Info?.ModDescription ?? "";
        TbUrl.Text = data?.UrlOverride ?? _entry.Info?.ProjectUrl ?? "";
        TbNotes.Text = data?.Notes ?? "";
        TbGbId.Text = data?.GameBananaId ?? "";

        // フレームワーク MOD は削除禁止
        BtnDelete.Visibility = _entry.IsLibrary ? Visibility.Collapsed : Visibility.Visible;

        // Config.json の有無に応じてボタン表示を切替
        BtnConfig.Visibility =
            (_vm.Install is { } inst && ModConfigStore.Exists(inst, _entry.ModId))
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveFields()
    {
        var ud = UserDataStore.Load(UserDataStore.DefaultPath);
        if (!ud.Mods.TryGetValue(_entry.ModId, out var data))
            data = new ModUserData();

        data.TranslatedName = TbName.Text.Trim();
        data.TranslatedDescription = TbDescription.Text.Trim();
        data.UrlOverride = TbUrl.Text.Trim();
        data.Notes = TbNotes.Text.Trim();
        var gbId = TbGbId.Text.Trim();
        data.GameBananaId = string.IsNullOrEmpty(gbId) ? null : gbId;

        ud.Mods[_entry.ModId] = data;
        UserDataStore.Save(UserDataStore.DefaultPath, ud);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        _vm.Reload();
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Install is not { } install) return;
        var win = new ModConfigWindow(_entry.ModId, _entry.DisplayName, install) { Owner = this };
        win.ShowDialog();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (_vm.RefreshSelectedAction is not null)
            _ = _vm.RefreshSelectedAction(new[] { _entry.ModId });
        Close();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_entry.Info is null)
        {
            MessageBox.Show("MOD フォルダが見つかりません。", "削除エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = _entry.DisplayName;
        var confirm = MessageBox.Show(
            $"「{name}」のフォルダをゴミ箱に移動します。よろしいですか？\n\n{_entry.Info.FolderPath}",
            "MOD 削除の確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        // 全ゲームの AppConfig.json から除去
        foreach (var game in _vm.Games)
        {
            var configPath = Path.Combine(game.FolderPath, "AppConfig.json");
            if (File.Exists(configPath))
                AppConfigWriter.RemoveMod(configPath, game.AppId, _entry.ModId);
        }

        // UserData から除去
        var ud = UserDataStore.Load(UserDataStore.DefaultPath);
        ud.Mods.Remove(_entry.ModId);
        UserDataStore.Save(UserDataStore.DefaultPath, ud);

        // ゴミ箱へ
        var success = RecycleBinHelper.SendToRecycleBin(_entry.Info.FolderPath);
        if (!success)
        {
            MessageBox.Show("ゴミ箱への移動に失敗しました。", "削除エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _vm.Reload();
        Close();
    }
}
