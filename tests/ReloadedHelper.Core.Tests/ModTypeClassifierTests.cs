// tests/ReloadedHelper.Core.Tests/ModTypeClassifierTests.cs
using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModTypeClassifierTests
{
    private static ModInfo Mod(string id, string name = "", string desc = "",
        string folder = "", bool lib = false) => new(
        id, name, "", "1.0", desc, System.Array.Empty<string>(), System.Array.Empty<string>(),
        System.Array.Empty<string>(), System.Array.Empty<string>(), null, null, null, null, folder, lib);

    private static string EmptyDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"mtc-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static string DirWith(string sub)
    {
        var d = EmptyDir();
        Directory.CreateDirectory(Path.Combine(d, sub.Replace('/', Path.DirectorySeparatorChar)));
        return d;
    }

    [Fact]
    public void Library_Wins()
        => Assert.Equal(ModType.Library, ModTypeClassifier.Classify(Mod("x", lib: true), "Skins").Type);

    [Fact]
    public void BgmeFolder_IsMusic()
    {
        var d = DirWith("BGME");
        try { Assert.Equal(ModType.Music, ModTypeClassifier.Classify(Mod("x", folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void CostumesFolder_IsCostume()
    {
        var d = DirWith("Costumes");
        try { Assert.Equal(ModType.Costume, ModTypeClassifier.Classify(Mod("x", folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Theory]
    [InlineData("Skins", ModType.SkinTexture)]
    [InlineData("Textures", ModType.SkinTexture)]
    [InlineData("Portraits", ModType.Portrait)]
    [InlineData("Models", ModType.Model)]
    [InlineData("Personas", ModType.Model)]
    [InlineData("User Interface", ModType.Ui)]
    [InlineData("Battles", ModType.Battle)]
    [InlineData("Cutscenes / FMV", ModType.Event)]
    public void SpecificCategory_Maps(string cat, ModType expected)
    {
        var d = EmptyDir();
        try { Assert.Equal(expected, ModTypeClassifier.Classify(Mod("x", folder: d), cat).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Theory]
    [InlineData("p5rpc.music.foundalight", "光を見つけた", ModType.Music)]
    [InlineData("x", "戦闘BGM差し替え", ModType.Music)]
    [InlineData("p5rpc.cheats.darts", "チート", ModType.Gameplay)]
    [InlineData("x", "Black Mask Boss Animations", ModType.Battle)]
    [InlineData("x", "PS4 cutscenes restored", ModType.Event)]
    [InlineData("p5rpc.skin.longhairann", "ロングヘアのアン", ModType.SkinTexture)]
    public void Keywords_Classify(string id, string name, ModType expected)
    {
        var d = EmptyDir();
        try { Assert.Equal(expected, ModTypeClassifier.Classify(Mod(id, name, folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void VagueCategory_IsGameplay()
    {
        var d = EmptyDir();
        try { Assert.Equal(ModType.Gameplay, ModTypeClassifier.Classify(Mod("x", folder: d), "Other/Misc").Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void CharactersCategory_IsModel()
    {
        var d = EmptyDir();
        try { Assert.Equal(ModType.Model, ModTypeClassifier.Classify(Mod("x", folder: d), "Characters").Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void FileReplaceOnly_IsSkinTexture()
    {
        var d = EmptyDir();
        var f = Path.Combine(d, "P5REssentials", "CPK", "BASE.CPK", "t.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, "x");
        try { Assert.Equal(ModType.SkinTexture, ModTypeClassifier.Classify(Mod("x", folder: d), null).Type); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void NoSignal_IsUnknown()
    {
        var d = EmptyDir();
        try
        {
            var r = ModTypeClassifier.Classify(Mod("x", folder: d), null);
            Assert.Equal(ModType.Unknown, r.Type);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }
        finally { Directory.Delete(d, true); }
    }
}
