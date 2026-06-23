using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModRoleClassifierTests
{
    private static ModInfo Mod(bool lib = false, params string[] tags) => new(
        "m", "M", "", "1.0", "", tags, Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), null, null, null, null, "", lib);

    [Theory]
    // 旧来の表記（後方互換）
    [InlineData("Skin", ModRole.VisualOverride)]
    [InlineData("Texture", ModRole.VisualOverride)]
    [InlineData("UI", ModRole.VisualOverride)]
    [InlineData("Sound", ModRole.Music)]
    [InlineData("Gameplay Mechanics", ModRole.BaseLayer)]
    [InlineData("Misc", ModRole.Unknown)]
    [InlineData(null, ModRole.Unknown)]
    // 実データの GameBanana カテゴリ（複数形・別名）
    [InlineData("Skins", ModRole.VisualOverride)]
    [InlineData("Textures", ModRole.VisualOverride)]
    [InlineData("Texture Packs", ModRole.VisualOverride)]
    [InlineData("User Interface", ModRole.VisualOverride)]
    [InlineData("Portraits", ModRole.VisualOverride)]
    [InlineData("Models", ModRole.VisualOverride)]
    [InlineData("Model Packs", ModRole.VisualOverride)]
    [InlineData("Personas", ModRole.VisualOverride)]
    [InlineData("Characters", ModRole.BaseLayer)]
    [InlineData("Cutscenes / FMV", ModRole.BaseLayer)]
    [InlineData("QOL", ModRole.Unknown)]
    [InlineData("Fixes", ModRole.Unknown)]
    [InlineData("Other/Misc", ModRole.Unknown)]
    // 大文字小文字・前後空白に強いこと
    [InlineData("skins", ModRole.VisualOverride)]
    [InlineData("  Textures  ", ModRole.VisualOverride)]
    public void Classify_ByCategory(string? category, ModRole expected)
        => Assert.Equal(expected, ModRoleClassifier.Classify(Mod(), category));

    [Fact]
    public void Classify_LibraryBeatsCategory()
        => Assert.Equal(ModRole.Library, ModRoleClassifier.Classify(Mod(lib: true), "Skin"));
}
