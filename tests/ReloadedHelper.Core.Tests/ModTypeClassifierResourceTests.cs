using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ModTypeClassifierResourceTests
{
    private static ModInfo Mod(string name) => new(
        "id", name, "", "1", "",
        System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>(),
        new[] { "p5r" }, null, null, null, null, "");

    [Fact]
    public void SongResource_WinsOverMisleadingName()
    {
        // 名前は音楽と無関係でも、曲資源を触るなら音楽。
        var res = new[] { new ResourceKey(ResourceKind.Song, "10") };
        Assert.Equal(ModType.Music, ModTypeClassifier.Classify(Mod("Cool Pack"), null, res).Type);
    }

    [Fact]
    public void CostumeResource_IsCostume()
    {
        var res = new[] { new ResourceKey(ResourceKind.Costume, "joker/0") };
        Assert.Equal(ModType.Costume, ModTypeClassifier.Classify(Mod("Pack"), null, res).Type);
    }

    [Fact]
    public void NoResources_FallsBackToExistingLogic()
    {
        // 資源が無ければ従来のキーワード判定（名前に "portrait"）。
        Assert.Equal(ModType.Portrait,
            ModTypeClassifier.Classify(Mod("Futaba portrait"), null, System.Array.Empty<ResourceKey>()).Type);
    }
}
