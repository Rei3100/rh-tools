using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public static class ModCatalog
{
    public static IReadOnlyDictionary<string, ModInfo> LoadAll(string modsDir)
    {
        var result = new Dictionary<string, ModInfo>(StringComparer.Ordinal);
        if (!Directory.Exists(modsDir)) return result;
        foreach (var folder in Directory.EnumerateDirectories(modsDir))
        {
            var cfg = Path.Combine(folder, "ModConfig.json");
            if (!File.Exists(cfg)) continue;
            try
            {
                var info = ModConfigParser.Parse(File.ReadAllText(cfg), folder);
                if (!string.IsNullOrEmpty(info.ModId)) result[info.ModId] = info;
            }
            catch (JsonException) { /* skip malformed mod */ }
        }
        return result;
    }
}

public static class GameCatalog
{
    public static IReadOnlyList<GameInfo> LoadAll(string appsDir)
    {
        var result = new List<GameInfo>();
        if (!Directory.Exists(appsDir)) return result;
        foreach (var folder in Directory.EnumerateDirectories(appsDir))
        {
            var cfg = Path.Combine(folder, "AppConfig.json");
            if (!File.Exists(cfg)) continue;
            try { result.Add(AppConfigParser.Parse(File.ReadAllText(cfg), folder)); }
            catch (JsonException) { /* skip malformed app */ }
        }
        return result;
    }
}
