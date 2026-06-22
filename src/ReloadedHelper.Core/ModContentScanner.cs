using System.IO;

namespace ReloadedHelper.Core;

public sealed record ModOverrides(string ModId, IReadOnlyList<string> Paths);

public static class ModContentScanner
{
    // 拡張ポイント: ゲームファイルを置き換える redirect ルート（相対・小文字比較）
    private static readonly string[] RedirectRoots = { "P5REssentials/CPK", "FEmulator/AWB" };

    public static ModOverrides Scan(string modFolderPath, string modId)
    {
        var paths = new List<string>();
        if (Directory.Exists(modFolderPath))
        {
            foreach (var root in RedirectRoots)
            {
                var rootDir = ResolveCaseInsensitive(modFolderPath, root);
                if (rootDir is null) continue;
                var rootKey = root.ToLowerInvariant();
                foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(rootDir, file).Replace('\\', '/').ToLowerInvariant();
                    paths.Add($"{rootKey}/{rel}");
                }
            }
        }
        return new ModOverrides(modId, paths);
    }

    // "A/B" を modFolderPath 配下から大文字小文字無視で解決。無ければ null。
    private static string? ResolveCaseInsensitive(string baseDir, string relative)
    {
        var current = baseDir;
        foreach (var seg in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current)) return null;
            var match = Directory.GetDirectories(current)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), seg, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            current = match;
        }
        return Directory.Exists(current) ? current : null;
    }
}
