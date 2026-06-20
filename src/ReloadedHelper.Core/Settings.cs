using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class AppSettings
{
    public string? ReloadedInstallPath { get; set; }
    public int UiZoomPercent { get; set; } = 100;
    public bool MinimizeToTray { get; set; } = true;
    public bool RememberLastGame { get; set; } = true;
    public string? LastGameId { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ReloadedHelper", "settings.json");

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
