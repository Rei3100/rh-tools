using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModLayerTests
{
    [Theory]
    [InlineData(ModRole.Library, 0)]
    [InlineData(ModRole.BaseLayer, 1)]
    [InlineData(ModRole.Music, 2)]
    [InlineData(ModRole.VisualOverride, 3)]
    [InlineData(ModRole.Unknown, 4)]
    public void Rank_OrdersWeakToStrong(ModRole role, int expected)
        => Assert.Equal(expected, ModLayer.Rank(role));

    [Fact]
    public void Label_IsNonEmptyJapanese()
    {
        Assert.False(string.IsNullOrWhiteSpace(ModLayer.Label(ModRole.Library)));
        Assert.False(string.IsNullOrWhiteSpace(ModLayer.Label(ModRole.Unknown)));
    }
}
