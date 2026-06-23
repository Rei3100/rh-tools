using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class FileReplaceAnalyzerTests
{
    private static ModInfo Mod(string folder) => new(
        "m", "M", "", "1.0", "", Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, folder);

    [Fact]
    public void Analyze_ReturnsFileResources_ForRedirectFiles()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var cpk = Path.Combine(tmp, "P5REssentials", "CPK", "sub");
        Directory.CreateDirectory(cpk);
        File.WriteAllText(Path.Combine(cpk, "Hair.bin"), "x");

        var res = new FileReplaceAnalyzer().Analyze(Mod(tmp));

        Assert.Contains(new ResourceKey(ResourceKind.File, "p5ressentials/cpk/sub/hair.bin"), res);
    }

    [Fact]
    public void Analyze_EmptyForNonExistentFolder()
        => Assert.Empty(new FileReplaceAnalyzer().Analyze(Mod(@"C:\nope\missing")));
}
