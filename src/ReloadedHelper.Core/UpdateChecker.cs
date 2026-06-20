// src/ReloadedHelper.Core/UpdateChecker.cs
using System.Net.Http;
using System.Text.Json;

namespace ReloadedHelper.Core;

public sealed class UpdateChecker(HttpClient http)
{
    private const string ApiUrl =
        "https://api.github.com/repos/Rei3100/rh-tools/releases/latest";

    public async Task<(string? Version, string? DownloadUrl)> CheckAsync()
    {
        try
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "ReloadedHelper/1.0");
            var json = await http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v');
            string? url = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            return (tag, url);
        }
        catch
        {
            return (null, null);
        }
    }
}
