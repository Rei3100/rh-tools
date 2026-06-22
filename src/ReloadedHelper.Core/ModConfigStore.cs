using System.IO;
using System.Text.Json;

namespace ReloadedHelper.Core;

public enum ConfigFieldKind { Bool, Number, Text }

public sealed record ModConfigField(string Name, ConfigFieldKind Kind, string Value);

public static class ModConfigStore
{
    public static string PathFor(ReloadedInstall install, string modId) =>
        Path.Combine(install.UserModsDir, modId, "Config.json");

    public static bool Exists(ReloadedInstall install, string modId) =>
        File.Exists(PathFor(install, modId));

    public static IReadOnlyList<ModConfigField> Read(string configPath)
    {
        if (!File.Exists(configPath)) return Array.Empty<ModConfigField>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<ModConfigField>();

            var result = new List<ModConfigField>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.True:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Bool, "true")); break;
                    case JsonValueKind.False:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Bool, "false")); break;
                    case JsonValueKind.Number:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Number, prop.Value.GetRawText())); break;
                    case JsonValueKind.String:
                        result.Add(new ModConfigField(prop.Name, ConfigFieldKind.Text, prop.Value.GetString() ?? "")); break;
                        // Object/Array/Null は対象外（Write で原本保持）
                }
            }
            return result;
        }
        catch (JsonException) { return Array.Empty<ModConfigField>(); }
        catch (IOException) { return Array.Empty<ModConfigField>(); }
    }
}
