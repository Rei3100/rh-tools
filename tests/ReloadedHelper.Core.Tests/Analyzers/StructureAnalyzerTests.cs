using System.IO;
using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class StructureAnalyzerTests
{
    private static ModInfo Mod(string folder) => new("m", "M", "", "1", "",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        null, null, null, null, folder);

    [Fact]
    public void Check_WarnsWhenRedirectRootNested_OneLevelTooDeep()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        // 余分な階層の下に CPK が埋もれている
        Directory.CreateDirectory(Path.Combine(tmp, "ExtraFolder", "P5REssentials", "CPK"));

        var warnings = StructureAnalyzer.Check(Mod(tmp));

        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Check_NoWarning_WhenRootAtTopLevel()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(tmp, "P5REssentials", "CPK"));

        Assert.Empty(StructureAnalyzer.Check(Mod(tmp)));
    }
}
