using System.Text.Json;

namespace ReloadedHelper.Core;

public static class AppConfigParser
{
    public static GameInfo Parse(string json, string folderPath)
    {
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;

            // Core string fields: default to "" if missing or non-string
            var appId = GetString(root, "AppId") ?? "";
            var appName = GetString(root, "AppName") ?? "";
            var appLocation = GetString(root, "AppLocation") ?? "";

            // Array fields: default to empty list if missing
            var enabledMods = GetStringArray(root, "EnabledMods");
            var sortedMods = GetStringArray(root, "SortedMods");

            // Icon file: empty string becomes null
            var iconFileName = GetString(root, "AppIcon");
            if (string.IsNullOrEmpty(iconFileName))
            {
                iconFileName = null;
            }

            return new GameInfo(
                appId,
                appName,
                appLocation,
                iconFileName,
                enabledMods,
                sortedMods,
                folderPath);
        }
    }

    /// <summary>
    /// Extracts a string value from a JSON element, returning null if the property
    /// is missing or not a string.
    /// </summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        if (prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return prop.GetString();
    }

    /// <summary>
    /// Extracts a string array from a JSON element, returning an empty list if
    /// the property is missing or not an array. Skips non-string entries.
    /// </summary>
    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return Array.Empty<string>();
        }

        if (prop.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var str = item.GetString();
                if (str != null)
                {
                    result.Add(str);
                }
            }
        }

        return result;
    }
}
