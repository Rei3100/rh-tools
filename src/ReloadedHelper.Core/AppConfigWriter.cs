// src/ReloadedHelper.Core/AppConfigWriter.cs
using System.Text.Json;

namespace ReloadedHelper.Core;

public static class AppConfigWriter
{
    public static void WriteOrder(
        string configPath, string appId, IReadOnlyList<string> newSortedMods)
    {
        LoadOrderBackupService.Backup(configPath, appId);

        var original = File.ReadAllBytes(configPath);
        using var doc = JsonDocument.Parse(original);

        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "SortedMods")
            {
                writer.WritePropertyName("SortedMods");
                writer.WriteStartArray();
                foreach (var mod in newSortedMods) writer.WriteStringValue(mod);
                writer.WriteEndArray();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();

        File.WriteAllBytes(configPath, ms.ToArray());
    }
}
