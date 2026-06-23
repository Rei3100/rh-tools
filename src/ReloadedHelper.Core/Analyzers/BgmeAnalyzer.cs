using System.IO;
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core.Analyzers;

/// <summary>
/// Analyzer for extracting song IDs from BGME .pme files.
/// Supports two patterns:
/// - Direct: "music = 12000"
/// - Random: "random_song(12000, 12001, ...)"
/// </summary>
public sealed class BgmeAnalyzer : IModAnalyzer
{
    // Direct pattern: music = <numeric ID>
    private static readonly Regex DirectPattern = new(@"^\s*music\s*=\s*(\d+)", RegexOptions.Compiled);

    // Random pattern: random_song(ID1, ID2, ...)
    private static readonly Regex RandomPattern = new(@"random_song\s*\(\s*([^)]+)\s*\)", RegexOptions.Compiled);

    public IReadOnlyList<ResourceKey> Analyze(ModInfo mod)
    {
        var dir = Path.Combine(mod.FolderPath, "BGME");
        if (!Directory.Exists(dir))
            return Array.Empty<ResourceKey>();

        var result = new List<ResourceKey>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.pme", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();

                // Skip comment lines
                if (trimmed.StartsWith("//"))
                    continue;

                // Try direct pattern: music = <id>
                var directMatch = DirectPattern.Match(line);
                if (directMatch.Success)
                {
                    result.Add(new ResourceKey(ResourceKind.Song, directMatch.Groups[1].Value));
                    continue;
                }

                // Try random pattern: random_song(<ids>)
                var randomMatch = RandomPattern.Match(line);
                if (randomMatch.Success)
                {
                    var ids = randomMatch.Groups[1].Value;
                    // Split by comma and extract numeric IDs
                    foreach (var part in ids.Split(','))
                    {
                        var id = part.Trim();
                        if (uint.TryParse(id, out _))
                        {
                            result.Add(new ResourceKey(ResourceKind.Song, id));
                        }
                    }
                }
            }
        }

        return result;
    }
}
