using ReloadedHelper.Core;

namespace ReloadedHelper.Core.Tests;

public class FilterModeTests
{
    private static readonly ModLoadEntry[] SampleEntries =
    [
        new ModLoadEntry(1, "ModA", null, Enabled: true),
        new ModLoadEntry(2, "ModB", null, Enabled: false),
        new ModLoadEntry(3, "ModC", null, Enabled: true),
    ];

    [Theory]
    [InlineData(FilterMode.All, 3)]
    [InlineData(FilterMode.EnabledOnly, 2)]
    [InlineData(FilterMode.DisabledOnly, 1)]
    public void Filter_ByMode_ReturnsCorrectCount(FilterMode mode, int expected)
    {
        var result = ModFilter.Filter(SampleEntries, null, mode);
        Assert.Equal(expected, result.Count);
    }

    [Fact]
    public void Filter_EnabledOnly_ReturnsOnlyEnabledEntries()
    {
        var result = ModFilter.Filter(SampleEntries, null, FilterMode.EnabledOnly);
        Assert.All(result, e => Assert.True(e.Enabled));
    }

    [Fact]
    public void Filter_DisabledOnly_ReturnsOnlyDisabledEntries()
    {
        var result = ModFilter.Filter(SampleEntries, null, FilterMode.DisabledOnly);
        Assert.All(result, e => Assert.False(e.Enabled));
    }

    [Fact]
    public void Filter_ModeAndSearch_BothApplied()
    {
        // Enabled かつ名前が "A" を含む → ModA のみ
        var result = ModFilter.Filter(SampleEntries, "A", FilterMode.EnabledOnly);
        Assert.Single(result);
        Assert.Equal("ModA", result[0].ModId);
    }

    [Fact]
    public void Filter_DefaultMode_IsAll()
    {
        // デフォルト引数 = All
        var result = ModFilter.Filter(SampleEntries, null);
        Assert.Equal(3, result.Count);
    }
}
