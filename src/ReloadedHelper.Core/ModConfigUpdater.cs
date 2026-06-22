using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ReloadedHelper.Core;

public static class ModConfigUpdater
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(string modFolderPath, string japaneseName, string japaneseDescription, string? author = null)
    {
        var configPath = Path.Combine(modFolderPath, "ModConfig.json");
        if (!File.Exists(configPath)) return;

        try
        {
            var original = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(original);

            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, WriterOptions))
            {
                writer.WriteStartObject();
                bool wroteModName = false, wroteModDescription = false, wroteAuthor = false;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "ModName")
                    {
                        writer.WriteString("ModName", japaneseName);
                        wroteModName = true;
                    }
                    else if (prop.Name == "ModDescription")
                    {
                        writer.WriteString("ModDescription", japaneseDescription);
                        wroteModDescription = true;
                    }
                    else if (prop.Name == "ModAuthor" && author is not null)
                    {
                        writer.WriteString("ModAuthor", author);
                        wroteAuthor = true;
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                // フィールドが元々なければ末尾に追加
                if (!wroteModName) writer.WriteString("ModName", japaneseName);
                if (!wroteModDescription) writer.WriteString("ModDescription", japaneseDescription);
                if (author is not null && !wroteAuthor) writer.WriteString("ModAuthor", author);
                writer.WriteEndObject();
            }

            File.WriteAllBytes(configPath, ms.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // スキップ、アプリ起動は継続
        }
    }
}
