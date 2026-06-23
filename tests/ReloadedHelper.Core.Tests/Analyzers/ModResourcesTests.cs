using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests.Analyzers;

public class ModResourcesTests
{
    [Fact]
    public void ResourceKey_ToString_UsesKindPrefix()
    {
        Assert.Equal("file:p5ressentials/cpk/a.bin",
            new ResourceKey(ResourceKind.File, "p5ressentials/cpk/a.bin").ToString());
        Assert.Equal("song:12000", new ResourceKey(ResourceKind.Song, "12000").ToString());
        Assert.Equal("costume:joker/0", new ResourceKey(ResourceKind.Costume, "joker/0").ToString());
    }

    [Fact]
    public void ResourceKey_EqualityIsValueBased()
    {
        Assert.Equal(new ResourceKey(ResourceKind.Song, "1"), new ResourceKey(ResourceKind.Song, "1"));
        Assert.NotEqual(new ResourceKey(ResourceKind.Song, "1"), new ResourceKey(ResourceKind.File, "1"));
    }
}
