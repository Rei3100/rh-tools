namespace ReloadedHelper.Core;

public enum RefreshStatus { GbMatched, TranslatedOnly, Failed }

public sealed record MetadataRefreshResult(
    string ModId, RefreshStatus Status, string? GbId,
    string JaName, string JaDesc, string? Category, string? Author, string Reason);

public sealed class ModMetadataRefresher(IGameBananaSource gb, ITranslator tr)
{
    public async Task<MetadataRefreshResult> RefreshAsync(
        ModInfo mod, ModUserData ud, CancellationToken ct = default)
    {
        // 英語原本を未設定時にスナップショット
        ud.OriginalName ??= mod.ModName;
        ud.OriginalDescription ??= mod.ModDescription;

        // ── GB ID の決定 ──
        var gbId = ud.GameBananaId
                   ?? gb.ExtractId(ud.UrlOverride)
                   ?? gb.ExtractId(mod.ProjectUrl);

        if (gbId is null)
        {
            var key = SearchKey(ud, mod);
            foreach (var gameId in GameRegistry.GameIdsFor(mod.SupportedAppIds))
            {
                var hit = await gb.SearchAsync(key, gameId, ct);
                if (hit is not null) { gbId = hit.Value.GbId; break; }
            }
        }

        // ── GB 一致時：GB データを翻訳 ──
        if (gbId is not null)
        {
            var info = await gb.FetchAsync(gbId, ct);
            if (info is not null)
            {
                var name = ApplyGlossary(await tr.TranslateAsync(info.Name, "ja", ct), mod);
                var desc = ApplyGlossary(await tr.TranslateAsync(HtmlText.Strip(info.Text), "ja", ct), mod);
                return new MetadataRefreshResult(mod.ModId, RefreshStatus.GbMatched, gbId,
                    name, desc, info.Category, info.Author, "GB一致");
            }
            // フェッチ失敗 → 翻訳フォールバックへ（理由を残す）
            var fn = ApplyGlossary(await tr.TranslateAsync(ud.OriginalName ?? mod.ModName, "ja", ct), mod);
            var fd = ApplyGlossary(await tr.TranslateAsync(ud.OriginalDescription ?? mod.ModDescription, "ja", ct), mod);
            return new MetadataRefreshResult(mod.ModId, RefreshStatus.Failed, gbId,
                fn, fd, null, null, "GB取得失敗(翻訳のみ適用)");
        }

        // ── マッチなし → 翻訳のみ ──
        var jn = ApplyGlossary(await tr.TranslateAsync(ud.OriginalName ?? mod.ModName, "ja", ct), mod);
        var jd = ApplyGlossary(await tr.TranslateAsync(ud.OriginalDescription ?? mod.ModDescription, "ja", ct), mod);
        return new MetadataRefreshResult(mod.ModId, RefreshStatus.TranslatedOnly, null,
            jn, jd, null, null, "GB未一致(翻訳のみ)");
    }

    private static string SearchKey(ModUserData ud, ModInfo mod)
    {
        var name = ud.OriginalName;
        if (!string.IsNullOrWhiteSpace(name) && IsMostlyAscii(name)) return name;
        return mod.ModId;
    }

    private static bool IsMostlyAscii(string s)
    {
        int ascii = s.Count(c => c < 128);
        return s.Length > 0 && (double)ascii / s.Length >= 0.7;
    }

    private static string ApplyGlossary(string text, ModInfo mod)
    {
        foreach (var appId in mod.SupportedAppIds)
            text = GlossaryProvider.Apply(text, appId);
        return text;
    }
}
