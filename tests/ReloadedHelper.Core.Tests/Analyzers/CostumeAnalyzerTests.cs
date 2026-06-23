using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class CostumeAnalyzerTests
{
    private static ModInfo Mod(string folder) => new("m", "M", "", "1", "",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        null, null, null, null, folder);

    [Fact]
    public void Analyze_ExtractsCostumeSlots()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(tmp, "Costumes", "Joker", "0"));
        Directory.CreateDirectory(Path.Combine(tmp, "Costumes", "Joker", "1"));

        var res = new CostumeAnalyzer().Analyze(Mod(tmp));

        Assert.Contains(new ResourceKey(ResourceKind.Costume, "joker/0"), res);
        Assert.Contains(new ResourceKey(ResourceKind.Costume, "joker/1"), res);
    }

    [Fact]
    public void Analyze_EmptyWhenNoCostumesFolder()
        => Assert.Empty(new CostumeAnalyzer().Analyze(Mod(Directory.CreateTempSubdirectory().FullName)));
}
