// tests/ReloadedHelper.Core.Tests/ModTypeTests.cs
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModTypeTests
{
    [Theory]
    [InlineData(ModType.Library, 0)]
    [InlineData(ModType.Gameplay, 1)]
    [InlineData(ModType.Battle, 2)]
    [InlineData(ModType.Event, 3)]
    [InlineData(ModType.Music, 4)]
    [InlineData(ModType.Unknown, 5)]
    [InlineData(ModType.Model, 6)]
    [InlineData(ModType.Costume, 7)]
    [InlineData(ModType.SkinTexture, 8)]
    [InlineData(ModType.Portrait, 9)]
    [InlineData(ModType.Ui, 10)]
    public void Rank_FrontToBack(ModType t, int expected)
        => Assert.Equal(expected, ModTypeInfo.Rank(t));

    [Fact]
    public void Label_IsNonEmptyForAll()
    {
        foreach (ModType t in System.Enum.GetValues<ModType>())
            Assert.False(string.IsNullOrWhiteSpace(ModTypeInfo.Label(t)));
    }
}
