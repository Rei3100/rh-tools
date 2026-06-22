using System.Text.Json;

namespace ReloadedHelper.Core;

public static class ModConfigParser
{
    public static ModInfo Parse(string json, string folderPath)
    {
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;

            // Core string fields: default to "" if missing or non-string
            var modId = GetString(root, "ModId") ?? "";
            var modName = GetString(root, "ModName") ?? "";
            var modAuthor = GetString(root, "ModAuthor") ?? "";
            var modVersion = GetString(root, "ModVersion") ?? "";
            var modDescription = GetString(root, "ModDescription") ?? "";

            // Array fields: default to empty list if missing
            var tags = GetStringArray(root, "Tags");
            var dependencies = GetStringArray(root, "ModDependencies");
            var optionalDependencies = GetStringArray(root, "OptionalDependencies");
            var supportedAppIds = GetStringArray(root, "SupportedAppId");

            // Optional URL field: null if missing or whitespace-only
            var projectUrl = GetString(root, "ProjectUrl");
            if (string.IsNullOrWhiteSpace(projectUrl))
            {
                projectUrl = null;
            }

            // GitHub metadata from nested path
            string? gitHubUserName = null;
            string? gitHubRepositoryName = null;
            if (root.TryGetProperty("PluginData", out var pluginData) &&
                pluginData.TryGetProperty("GitHubRelease", out var githubRelease))
            {
                gitHubUserName = GetString(githubRelease, "UserName");
                gitHubRepositoryName = GetString(githubRelease, "RepositoryName");
            }

            // Icon file: empty string becomes null
            var iconFileName = GetString(root, "ModIcon");
            if (string.IsNullOrEmpty(iconFileName))
            {
                iconFileName = null;
            }

            // IsLibrary flag: defaults to false if missing
            var isLibrary = GetBool(root, "IsLibrary");

            return new ModInfo(
                modId,
                modName,
                modAuthor,
                modVersion,
                modDescription,
                tags,
                dependencies,
                optionalDependencies,
                supportedAppIds,
                projectUrl,
                gitHubUserName,
                gitHubRepositoryName,
                iconFileName,
                folderPath,
                isLibrary);
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

    /// <summary>
    /// Extracts a boolean value from a JSON element, returning false if the property
    /// is missing or not a boolean.
    /// </summary>
    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return false;
        }

        return prop.ValueKind == JsonValueKind.True;
    }
}
