namespace ReloadedHelper.Core;

public static class GameRegistry
{
    // AppId（小文字）→ GameBanana ゲームID（複数可・優先順）
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["p5r.exe"] = new[] { "16951" },
        ["p4g.exe"] = new[] { "17755", "8263" },
        ["p5s.exe"] = new[] { "9099" },
    };

    public static IReadOnlyList<string> GameIdsFor(IEnumerable<string> appIds)
    {
        var result = new List<string>();
        foreach (var appId in appIds)
            if (Map.TryGetValue(appId, out var ids))
                foreach (var id in ids)
                    if (!result.Contains(id)) result.Add(id);
        return result;
    }
}
