using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModRoleClassifierTests
{
    private static ModInfo Mod(bool lib = false, params string[] tags) => new(
        "m", "M", "", "1.0", "", tags, Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), null, null, null, null, "", lib);

    [Theory]
    [InlineData("Skin", ModRole.VisualOverride)]
    [InlineData("Texture", ModRole.VisualOverride)]
    [InlineData("UI", ModRole.VisualOverride)]
    [InlineData("Sound", ModRole.Music)]
    [InlineData("Gameplay Mechanics", ModRole.BaseLayer)]
    [InlineData("Misc", ModRole.Unknown)]
    [InlineData(null, ModRole.Unknown)]
    public void Classify_ByCategory(string? category, ModRole expected)
        => Assert.Equal(expected, ModRoleClassifier.Classify(Mod(), category));

    [Fact]
    public void Classify_LibraryBeatsCategory()
        => Assert.Equal(ModRole.Library, ModRoleClassifier.Classify(Mod(lib: true), "Skin"));
}
