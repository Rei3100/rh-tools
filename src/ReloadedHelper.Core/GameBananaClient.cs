using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public sealed record GameBananaModInfo(string Name, string Text, string? Category, string GameId);

public sealed class GameBananaClient(HttpClient http)
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(3);
    private const double SimilarityThreshold = 0.80;

    public static string? ExtractIdFromUrl(string? projectUrl)
    {
        if (string.IsNullOrEmpty(projectUrl)) return null;
        var m = Regex.Match(projectUrl, @"gamebanana\.com/(?:mods|dl)/(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public async Task<GameBananaModInfo?> FetchAsync(string gbId, CancellationToken ct = default)
    {
        var url = $"https://api.gamebanana.com/apiv11/Mod/{gbId}/ProfilePage" +
                  "?fields=name%2Ctext%2CCategory().name%2CGame().id";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ApiTimeout);
            var json = await http.GetStringAsync(url, cts.Token);
            return ParseModInfo(json);
        }
        catch { return null; }
    }

    public async Task<(string GbId, string GbGameId)?> SearchAsync(
        string modName, string gbGameId, CancellationToken ct = default)
    {
        var q = Uri.EscapeDataString(modName);
        var url = $"https://api.gamebanana.com/apiv11/Util/Search/Results" +
                  $"?search_query={q}&itemtype=Mod&gameid={gbGameId}&page=1&nperpage=5";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ApiTimeout);
            var json = await http.GetStringAsync(url, cts.Token);
            return ParseSearchResult(json, modName, gbGameId);
        }
        catch { return null; }
    }

    private static GameBananaModInfo? ParseModInfo(string json)
    {
        // 期待形式: ["name", "text", "Category", "gameId"]
        try
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 4) return null;

            var name     = arr[0].GetString() ?? "";
            var text     = arr[1].GetString() ?? "";
            var category = arr[2].ValueKind == JsonValueKind.String ? arr[2].GetString() : null;
            var gameId   = arr[3].ValueKind == JsonValueKind.String
                ? arr[3].GetString() ?? ""
                : arr[3].GetInt64().ToString();

            return new GameBananaModInfo(name, text, category, gameId);
        }
        catch (JsonException) { return null; }
    }

    private static (string GbId, string GbGameId)? ParseSearchResult(
        string json, string modName, string gbGameId)
    {
        // 期待形式: [{"_idRow": 123, "_sName": "..."}, ...]
        try
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array) return null;

            string? bestId = null;
            double bestScore = 0;

            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("_idRow", out var idProp)) continue;
                if (!item.TryGetProperty("_sName", out var nameProp)) continue;

                var id   = idProp.ValueKind == JsonValueKind.Number
                    ? idProp.GetInt64().ToString()
                    : idProp.GetString() ?? "";
                var name = nameProp.GetString() ?? "";
                var score = Similarity(modName, name);

                if (score > bestScore) { bestScore = score; bestId = id; }
            }

            if (bestScore >= SimilarityThreshold && bestId is not null)
                return (bestId, gbGameId);
            return null;
        }
        catch (JsonException) { return null; }
    }

    private static double Similarity(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 && nb.Length == 0) return 1.0;
        if (na.Length == 0 || nb.Length == 0) return 0.0;
        int dist = LevenshteinDistance(na, nb);
        return 1.0 - (double)dist / Math.Max(na.Length, nb.Length);
    }

    private static string Normalize(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static int LevenshteinDistance(string a, string b)
    {
        int m = a.Length, n = b.Length;
        var d = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                d[i, j] = a[i - 1] == b[j - 1]
                    ? d[i - 1, j - 1]
                    : 1 + Math.Min(d[i - 1, j], Math.Min(d[i, j - 1], d[i - 1, j - 1]));
        return d[m, n];
    }
}
