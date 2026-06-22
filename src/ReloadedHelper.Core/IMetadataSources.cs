namespace ReloadedHelper.Core;

public interface IGameBananaSource
{
    string? ExtractId(string? url);
    Task<(string GbId, string GbGameId)?> SearchAsync(string modName, string gbGameId, CancellationToken ct = default);
    Task<GameBananaModInfo?> FetchAsync(string gbId, CancellationToken ct = default);
}

public interface ITranslator
{
    Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct = default);
}
