// tests/ReloadedHelper.Core.Tests/ModConfigParserIsLibraryTests.cs
namespace ReloadedHelper.Core.Tests;

public class ModConfigParserIsLibraryTests
{
    [Fact]
    public void Parse_WhenIsLibraryTrue_ReturnsTrue()
    {
        const string json = """
            {
                "ModId": "lib.mod", "ModName": "Lib", "ModAuthor": "A",
                "ModVersion": "1.0", "ModDescription": "Desc",
                "IsLibrary": true
            }
            """;
        var result = ModConfigParser.Parse(json, @"C:\mods\lib");
        Assert.True(result.IsLibrary);
    }

    [Fact]
    public void Parse_WhenIsLibraryMissing_ReturnsFalse()
    {
        const string json = """
            { "ModId": "normal.mod", "ModName": "Normal" }
            """;
        var result = ModConfigParser.Parse(json, @"C:\mods\normal");
        Assert.False(result.IsLibrary);
    }

    [Fact]
    public void Parse_WhenIsLibraryFalse_ReturnsFalse()
    {
        const string json = """
            { "ModId": "normal.mod", "ModName": "Normal", "IsLibrary": false }
            """;
        var result = ModConfigParser.Parse(json, @"C:\mods\normal");
        Assert.False(result.IsLibrary);
    }

    [Fact]
    public void ModLoadEntry_WhenIsLibraryTrue_CategoryLabelIsFramework()
    {
        var entry = new ModLoadEntry(1, "lib.mod", null, true, "Sound", IsLibrary: true);
        Assert.Equal("フレームワーク", entry.CategoryLabel);
    }

    [Fact]
    public void ModLoadEntry_WhenIsLibraryFalse_CategoryLabelUsesCategory()
    {
        var entry = new ModLoadEntry(1, "sound.mod", null, true, "Sound", IsLibrary: false);
        Assert.Equal("サウンド", entry.CategoryLabel);
    }

    [Fact]
    public void ModLoadEntry_WhenIsLibraryFalse_NullCategory_CategoryLabelIsNull()
    {
        var entry = new ModLoadEntry(1, "mod", null, true, null, IsLibrary: false);
        Assert.Null(entry.CategoryLabel);
    }
}
