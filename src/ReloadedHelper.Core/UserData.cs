using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class ModUserData
{
    public string? TranslatedName { get; set; }
    public string? TranslatedDescription { get; set; }
    public string? UrlOverride { get; set; }
    public string? Notes { get; set; }
}

public sealed class UserDataFile
{
    public Dictionary<string, ModUserData> Mods { get; set; } = new();
}

public static class UserDataStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ReloadedHelper", "userdata.json");

    public static UserDataFile Load(string path)
    {
        if (!File.Exists(path)) return new UserDataFile();
        try { return JsonSerializer.Deserialize<UserDataFile>(File.ReadAllText(path)) ?? new UserDataFile(); }
        catch (JsonException) { return new UserDataFile(); }
    }

    public static void Save(string path, UserDataFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }
}
