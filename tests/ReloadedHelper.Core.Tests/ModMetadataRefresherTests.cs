namespace ReloadedHelper.Core.Tests;

public class ModMetadataRefresherTests
{
    private static ModInfo Mod(string id, string name, string desc = "", string? url = null,
        string[]? appIds = null) =>
        new(id, name, "", "1.0.0", desc, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), appIds ?? new[] { "p5r.exe" }, url, null, null, null, "C:\\x");

    // テスト用フェイク
    private sealed class FakeGb : IGameBananaSource
    {
        public string? IdFromUrl; public (string, string)? SearchHit; public GameBananaModInfo? FetchResult;
        public string? LastSearchName;
        public string? ExtractId(string? url) => IdFromUrl;
        public Task<(string GbId, string GbGameId)?> SearchAsync(string n, string g, CancellationToken ct = default)
        { LastSearchName = n; return Task.FromResult(SearchHit); }
        public Task<GameBananaModInfo?> FetchAsync(string id, CancellationToken ct = default)
            => Task.FromResult(FetchResult);
    }
    private sealed class FakeTr : ITranslator
    {
        public Task<string> TranslateAsync(string t, string lang, CancellationToken ct = default)
            => Task.FromResult("[訳]" + t);
    }

    [Fact]
    public async Task GbMatched_uses_gamebanana_data()
    {
        var gb = new FakeGb
        {
            IdFromUrl = "491359",
            FetchResult = new GameBananaModInfo("Beta Lavenza", "restores model<br>warn", "Skin", "16951", "lonelycrow")
        };
        var sut = new ModMetadataRefresher(gb, new FakeTr());
        var ud = new ModUserData();

        var r = await sut.RefreshAsync(Mod("m1", "ベータ ラヴェンツァ", url: "https://gamebanana.com/mods/491359", appIds: new[] { "other.exe" }), ud);

        Assert.Equal(RefreshStatus.GbMatched, r.Status);
        Assert.Equal("491359", r.GbId);
        Assert.Equal("[訳]Beta Lavenza", r.JaName);
        Assert.Equal("[訳]restores model\nwarn", r.JaDesc); // HTML 除去後に翻訳
        Assert.Equal("Skin", r.Category);
        Assert.Equal("lonelycrow", r.Author);
    }

    [Fact]
    public async Task NoMatch_falls_back_to_translation_only_and_snapshots_original()
    {
        var gb = new FakeGb { IdFromUrl = null, SearchHit = null };
        var sut = new ModMetadataRefresher(gb, new FakeTr());
        var ud = new ModUserData();

        var r = await sut.RefreshAsync(Mod("m2", "Cool English Name", "english desc"), ud);

        Assert.Equal(RefreshStatus.TranslatedOnly, r.Status);
        Assert.Null(r.GbId);
        Assert.Equal("[訳]Cool English Name", r.JaName);
        Assert.Equal("Cool English Name", ud.OriginalName);       // スナップショット
        Assert.Equal("english desc", ud.OriginalDescription);
    }

    [Fact]
    public async Task Search_uses_english_modid_when_name_is_japanese()
    {
        var gb = new FakeGb { IdFromUrl = null, SearchHit = ("777", "16951") };
        var sut = new ModMetadataRefresher(gb, new FakeTr());
        // 現在名が日本語、OriginalName 未設定 → 検索キーは ModId
        await sut.RefreshAsync(Mod("P5R.CostumeFramework", "コスチューム"), new ModUserData());
        Assert.Equal("P5R.CostumeFramework", gb.LastSearchName);
    }
}
