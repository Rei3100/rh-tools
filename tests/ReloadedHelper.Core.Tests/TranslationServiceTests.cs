namespace ReloadedHelper.Core.Tests;

public class TranslationServiceTests
{
    // Google Translate 非公式 API のレスポンス形式:
    // [[["翻訳テキスト", "原文", ...], ...], null, "en"]
    private static string TranslateResponse(string translated, string original) =>
        $"[[[{System.Text.Json.JsonSerializer.Serialize(translated)},{System.Text.Json.JsonSerializer.Serialize(original)}]],null,\"en\"]";

    [Fact]
    public async Task TranslateAsync_returns_translated_text()
    {
        var handler = new FakeHttpMessageHandler(TranslateResponse("ジョーカー", "Joker"));
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("Joker", "ja");

        Assert.Equal("ジョーカー", result);
        Assert.Contains("translate.googleapis.com", handler.LastRequestUri);
        Assert.Contains("Joker", handler.LastRequestUri);
        Assert.Contains("tl=ja", handler.LastRequestUri);
    }

    [Fact]
    public async Task TranslateAsync_returns_original_text_on_http_failure()
    {
        var handler = new FakeHttpMessageHandler("", System.Net.HttpStatusCode.ServiceUnavailable);
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("Hello", "ja");

        Assert.Equal("Hello", result);
    }

    [Fact]
    public async Task TranslateAsync_handles_multi_chunk_response()
    {
        // 長いテキストは複数チャンクに分割されて返ることがある
        var json = "[[[\"こんにちは\",\"Hello\"],[\"世界\",\"world\"]],null,\"en\"]";
        var handler = new FakeHttpMessageHandler(json);
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("Hello world", "ja");

        Assert.Equal("こんにちは世界", result);
    }

    [Fact]
    public async Task TranslateAsync_returns_empty_for_empty_input()
    {
        var handler = new FakeHttpMessageHandler("");
        var http = new HttpClient(handler);
        var svc = new TranslationService(http, TimeSpan.Zero);

        var result = await svc.TranslateAsync("", "ja");

        Assert.Equal("", result);
    }
}
