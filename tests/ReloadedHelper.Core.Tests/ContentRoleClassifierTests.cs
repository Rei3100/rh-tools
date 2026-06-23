// tests/ReloadedHelper.Core.Tests/ContentRoleClassifierTests.cs
using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ContentRoleClassifierTests
{
    private static ModInfo Mod(string folder, bool lib = false) => new(
        "m", "M", "", "1.0", "", Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, folder, lib);

    private static string MakeDir(params string[] subdirs)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crc-{Guid.NewGuid():N}");
        foreach (var s in subdirs)
            Directory.CreateDirectory(Path.Combine(dir, s.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Library_Wins_Regardless()
    {
        var d = Make_EmptyDir();
        try
        {
            var r = ContentRoleClassifier.Classify(Mod(d, lib: true), "Skin");
            Assert.Equal(ModRole.Library, r.Role);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void BgmeFolder_IsMusic_EvenWithoutCategory()
    {
        var d = MakeDir("BGME");
        try { Assert.Equal(ModRole.Music, ContentRoleClassifier.Classify(Mod(d), null).Role); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void CostumesFolder_IsVisual_EvenWithoutCategory()
    {
        var d = MakeDir("Costumes");
        try { Assert.Equal(ModRole.VisualOverride, ContentRoleClassifier.Classify(Mod(d), null).Role); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void Category_UsedWhenNoFolderSignal()
    {
        var d = Make_EmptyDir();
        try { Assert.Equal(ModRole.BaseLayer, ContentRoleClassifier.Classify(Mod(d), "Characters").Role); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void FileReplacingMod_WithoutCategory_IsVisual()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crc-{Guid.NewGuid():N}");
        var full = Path.Combine(dir, "P5REssentials", "CPK", "BASE.CPK", "tex.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        try { Assert.Equal(ModRole.VisualOverride, ContentRoleClassifier.Classify(Mod(dir), null).Role); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NoSignal_IsUnknown()
    {
        var d = Make_EmptyDir();
        try { Assert.Equal(ModRole.Unknown, ContentRoleClassifier.Classify(Mod(d), null).Role); }
        finally { Directory.Delete(d, true); }
    }

    private static string Make_EmptyDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
