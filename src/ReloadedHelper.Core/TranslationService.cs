using System.Net.Http;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class TranslationService(HttpClient http, TimeSpan? requestDelay = null) : ITranslator
{
    private readonly TimeSpan _delay = requestDelay ?? TimeSpan.FromMilliseconds(100);

    public async Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, ct);

        var encoded = Uri.EscapeDataString(text);
        var url = $"https://translate.googleapis.com/translate_a/single" +
                  $"?client=gtx&sl=en&tl={targetLang}&dt=t&q={encoded}";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var response = await http.GetStringAsync(url, cts.Token);
                return ParseTranslation(response) ?? text;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (attempt == 1 || ct.IsCancellationRequested) return text;
                await Task.Delay(_delay > TimeSpan.Zero ? _delay : TimeSpan.FromMilliseconds(100), ct);
            }
        }
        return text;
    }

    private static string? ParseTranslation(string json)
    {
        // レスポンス形式: [[["翻訳テキスト","原文",...],...]],null,"en"]
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();
            foreach (var chunk in doc.RootElement[0].EnumerateArray())
            {
                if (chunk.GetArrayLength() > 0 && chunk[0].ValueKind == JsonValueKind.String)
                    sb.Append(chunk[0].GetString());
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (JsonException) { return null; }
    }
}
