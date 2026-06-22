using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

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
        var url = $"https://gamebanana.com/apiv11/Mod/{gbId}" +
                  "?_csvProperties=_sName,_sText,_aCategory,_aGame,_aSubmitter";
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
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var name = GetStr(root, "_sName") ?? "";
            var text = GetStr(root, "_sText") ?? "";
            string? category = root.TryGetProperty("_aCategory", out var cat) && cat.ValueKind == JsonValueKind.Object
                ? GetStr(cat, "_sName") : null;
            string gameId = "";
            if (root.TryGetProperty("_aGame", out var game) && game.ValueKind == JsonValueKind.Object
                && game.TryGetProperty("_idRow", out var gid))
                gameId = gid.ValueKind == JsonValueKind.Number ? gid.GetInt64().ToString() : (gid.GetString() ?? "");
            string? author = root.TryGetProperty("_aSubmitter", out var sub) && sub.ValueKind == JsonValueKind.Object
                ? GetStr(sub, "_sName") : null;

            return new GameBananaModInfo(name, text, category, gameId, author);
        }
        catch (JsonException) { return null; }
    }

    private static string? GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

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

                var id = idProp.ValueKind == JsonValueKind.Number
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
