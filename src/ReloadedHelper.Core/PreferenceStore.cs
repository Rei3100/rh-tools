using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class PreferenceStore
{
    private readonly string _path;
    // key: "appId|min|max"(小文字), value: winnerModId
    private readonly Dictionary<string, string> _winners;

    public PreferenceStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "preferences.json");
        try
        {
            _winners = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
                  ?? new(StringComparer.Ordinal)
                : new(StringComparer.Ordinal);
        }
        catch
        {
            _winners = new(StringComparer.Ordinal);
        }
    }

    private static string Key(string appId, string a, string b)
    {
        var x = a.ToLowerInvariant();
        var y = b.ToLowerInvariant();
        var (lo, hi) = string.CompareOrdinal(x, y) <= 0 ? (x, y) : (y, x);
        return $"{appId.ToLowerInvariant()}|{lo}|{hi}";
    }

    public string? GetWinner(string appId, string a, string b)
        => _winners.TryGetValue(Key(appId, a, b), out var w) ? w : null;

    public void SetWinner(string appId, string a, string b, string winnerModId)
    {
        _winners[Key(appId, a, b)] = winnerModId;
        Save();
    }

    public void Save() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(_winners,
            new JsonSerializerOptions { WriteIndented = true }));
}
