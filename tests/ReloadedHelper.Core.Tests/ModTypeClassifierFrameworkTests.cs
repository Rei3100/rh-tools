using ReloadedHelper.Core;
using ReloadedHelper.Core.Analyzers;
using Xunit;

namespace ReloadedHelper.Core.Tests;

// 実機(P5R)で判明した誤分類の再発防止：
// modloader(依存144)→戦闘, crifs.v2.hook(依存25)→末尾, CostumeFramework(依存29)→音楽 等を、
// 「多数から依存される＋資源ゼロ＝土台」で正す。説明文キーワードの暴発も止める。
public class ModTypeClassifierFrameworkTests
{
    private static ModInfo Mod(string name, string desc = "") => new(
        "id", name, "", "1", desc,
        System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>(),
        new[] { "p5r" }, null, null, null, null, "");

    private static readonly ResourceKey[] None = System.Array.Empty<ResourceKey>();
    private static readonly ResourceKey[] OneFile = { new(ResourceKind.File, "a/b.bin") };

    [Fact]
    public void DependedUpon_NoResources_IsFrameworkLibrary()
    {
        // 「戦闘」等の名前でも、多数から依存され資源を持たないなら土台。
        var d = ModTypeClassifier.Classify(Mod("Boss Battle Loader"), null, None, dependentsCount: 5);
        Assert.Equal(ModType.Library, d.Type);
    }

    [Fact]
    public void DependedUpon_ButHasResources_IsNotFramework()
    {
        // 資源を持つ人気コンテンツ（パッチ多数のスキン等）は土台にしない。
        var d = ModTypeClassifier.Classify(Mod("Long Hair Ann skin"), null, OneFile, dependentsCount: 11);
        Assert.NotEqual(ModType.Library, d.Type);
    }

    [Fact]
    public void SingleDependent_NoResources_IsFrameworkLibrary()
    {
        // 依存1でも資源0なら前提物（誰かに参照される土台）。
        var d = ModTypeClassifier.Classify(Mod("Texture Fixes Project"), null, None, dependentsCount: 1);
        Assert.Equal(ModType.Library, d.Type);
    }

    [Fact]
    public void ZeroDependents_NoResources_IsNotForcedFramework()
    {
        // 誰からも依存されないなら土台扱いしない。
        var d = ModTypeClassifier.Classify(Mod("Some Mod"), null, None, dependentsCount: 0);
        Assert.NotEqual(ModType.Library, d.Type);
    }

    [Fact]
    public void Description_MentionsMusic_ButNameDoesNot_IsNotMusic()
    {
        // 衣装フレームワークの説明文が "BGM/music" だらけ → 説明文を読むと音楽に暴発する。
        // 名前にしか手がかりを置かないので、音楽にはならない。
        var d = ModTypeClassifier.Classify(
            Mod("DLC Manager", "この衣装はBGMEフレームワークでmusicとBGMと音楽をカスタマイズできます"),
            null, None, dependentsCount: 0);
        Assert.NotEqual(ModType.Music, d.Type);
    }
}
