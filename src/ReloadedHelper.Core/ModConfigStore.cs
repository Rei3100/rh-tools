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

    public static void Write(string configPath, IReadOnlyList<ModConfigField> fields)
    {
        if (!File.Exists(configPath)) return;
        var byName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
            {
                w.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (byName.TryGetValue(prop.Name, out var f) && IsScalar(prop.Value.ValueKind))
                    {
                        w.WritePropertyName(prop.Name);
                        WriteValue(w, f);
                    }
                    else
                    {
                        prop.WriteTo(w); // 未対応 or fields 未指定 → 原本保持
                    }
                }
                w.WriteEndObject();
            }
            File.WriteAllBytes(configPath, ms.ToArray());
        }
        catch (JsonException) { }
        catch (IOException) { }
    }

    private static bool IsScalar(JsonValueKind k) =>
        k is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number or JsonValueKind.String;

    private static void WriteValue(Utf8JsonWriter w, ModConfigField f)
    {
        switch (f.Kind)
        {
            case ConfigFieldKind.Bool:
                w.WriteBooleanValue(bool.TryParse(f.Value, out var b) && b);
                break;
            case ConfigFieldKind.Number:
                if (long.TryParse(f.Value, out var l)) w.WriteNumberValue(l);
                else if (double.TryParse(f.Value, System.Globalization.CultureInfo.InvariantCulture, out var d)) w.WriteNumberValue(d);
                else w.WriteNumberValue(0);
                break;
            default:
                w.WriteStringValue(f.Value);
                break;
        }
    }
}
