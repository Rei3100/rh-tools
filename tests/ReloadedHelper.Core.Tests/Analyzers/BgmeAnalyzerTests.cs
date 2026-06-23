using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class BgmeAnalyzerTests
{
    private static ModInfo Mod(string folder) => new("m", "M", "", "1", "",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        null, null, null, null, folder);

    [Fact]
    public void Analyze_ExtractsSongIds_FromDirectMusicPattern()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var bgme = Path.Combine(tmp, "BGME");
        Directory.CreateDirectory(bgme);
        File.WriteAllText(Path.Combine(bgme, "Music.pme"),
            "// comment\nmusic = 12000\nmusic = 12001\n");

        var res = new BgmeAnalyzer().Analyze(Mod(tmp));

        Assert.Contains(new ResourceKey(ResourceKind.Song, "12000"), res);
        Assert.Contains(new ResourceKey(ResourceKind.Song, "12001"), res);
    }

    [Fact]
    public void Analyze_EmptyWhenNoBgmeFolder()
        => Assert.Empty(new BgmeAnalyzer().Analyze(Mod(Directory.CreateTempSubdirectory().FullName)));

    [Fact]
    public void Analyze_ExtractsSongIds_FromRandomSongPattern()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var bgme = Path.Combine(tmp, "BGME");
        Directory.CreateDirectory(bgme);
        File.WriteAllText(Path.Combine(bgme, "Random.pme"),
            "random_song(12000, 12001, 12002)\n");

        var res = new BgmeAnalyzer().Analyze(Mod(tmp));

        Assert.Contains(new ResourceKey(ResourceKind.Song, "12000"), res);
        Assert.Contains(new ResourceKey(ResourceKind.Song, "12001"), res);
        Assert.Contains(new ResourceKey(ResourceKind.Song, "12002"), res);
    }

    [Fact]
    public void Analyze_IgnoresNonNumericMusicValues()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var bgme = Path.Combine(tmp, "BGME");
        Directory.CreateDirectory(bgme);
        File.WriteAllText(Path.Combine(bgme, "Music.pme"),
            "music = default\nmusic = 12000\n");

        var res = new BgmeAnalyzer().Analyze(Mod(tmp));

        Assert.Single(res);
        Assert.Contains(new ResourceKey(ResourceKind.Song, "12000"), res);
    }
}
