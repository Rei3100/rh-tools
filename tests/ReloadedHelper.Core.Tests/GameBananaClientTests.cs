namespace ReloadedHelper.Core.Tests;

public class GameBananaClientTests
{
    [Theory]
    [InlineData("https://gamebanana.com/mods/123456", "123456")]
    [InlineData("https://gamebanana.com/dl/123456", "123456")]
    [InlineData("https://gamebanana.com/mods/123456?some=param", "123456")]
    [InlineData("https://example.com/other", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void ExtractIdFromUrl_parses_known_url_patterns(string? url, string? expected)
    {
        Assert.Equal(expected, GameBananaClient.ExtractIdFromUrl(url));
    }

    [Fact]
    public async Task FetchAsync_parses_apiv11_object_response()
    {
        var json = """
        {"_sName":"Persona 5 2016 Beta Lavenza",
         "_sText":"This mod restores Lavenza's old beta model.<br><br>Warning",
         "_aCategory":{"_sName":"Characters"},
         "_aGame":{"_idRow":16951},
         "_aSubmitter":{"_sName":"lonelycrow"}}
        """;
        var handler = new FakeHttpMessageHandler(json);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.FetchAsync("491359");

        Assert.NotNull(result);
        Assert.Equal("Persona 5 2016 Beta Lavenza", result!.Name);
        Assert.StartsWith("This mod restores", result.Text);
        Assert.Equal("Characters", result.Category);
        Assert.Equal("16951", result.GameId);
        Assert.Equal("lonelycrow", result.Author);
        Assert.Contains("gamebanana.com/apiv11/Mod/491359", handler.LastRequestUri);
        Assert.DoesNotContain("api.gamebanana.com", handler.LastRequestUri);
    }

    [Fact]
    public async Task FetchAsync_returns_null_on_failure()
    {
        var handler = new FakeHttpMessageHandler("", System.Net.HttpStatusCode.NotFound);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.FetchAsync("999");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_returns_best_match_above_80_percent()
    {
        // 完全一致 → 採用
        var json = """[{"_idRow": 123456, "_sName": "CRI FileSystem V2 Hook"}]""";
        var handler = new FakeHttpMessageHandler(json);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.SearchAsync("CRI FileSystem V2 Hook", "8809");

        Assert.NotNull(result);
        Assert.Equal("123456", result!.Value.GbId);
        Assert.Equal("8809", result!.Value.GbGameId);
    }

    [Fact]
    public async Task SearchAsync_returns_null_when_no_match_above_threshold()
    {
        var json = """[{"_idRow": 1, "_sName": "Completely Different Mod"}]""";
        var handler = new FakeHttpMessageHandler(json);
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.SearchAsync("My Unique Mod Name", "8809");

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_returns_null_on_empty_results()
    {
        var handler = new FakeHttpMessageHandler("[]");
        var client = new GameBananaClient(new System.Net.Http.HttpClient(handler));

        var result = await client.SearchAsync("Any Mod", "8809");

        Assert.Null(result);
    }
}
