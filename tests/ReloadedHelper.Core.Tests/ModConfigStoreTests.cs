using System.IO;
namespace ReloadedHelper.Core.Tests;

public class ModConfigStoreTests
{
    private static string WriteTemp(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Config.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Read_parses_scalar_fields_with_kinds()
    {
        var path = WriteTemp("""
        {"HotReload":false,"LogLevel":"Information","BaseBgmId_P5R":12000,"Volume":0.5}
        """);
        var fields = ModConfigStore.Read(path);

        Assert.Equal(4, fields.Count);
        Assert.Equal(new ModConfigField("HotReload", ConfigFieldKind.Bool, "false"), fields[0]);
        Assert.Equal(new ModConfigField("LogLevel", ConfigFieldKind.Text, "Information"), fields[1]);
        Assert.Equal(new ModConfigField("BaseBgmId_P5R", ConfigFieldKind.Number, "12000"), fields[2]);
        Assert.Equal(new ModConfigField("Volume", ConfigFieldKind.Number, "0.5"), fields[3]);
    }

    [Fact]
    public void Read_returns_empty_for_missing_or_invalid()
    {
        Assert.Empty(ModConfigStore.Read(Path.Combine(Path.GetTempPath(), "nope-Config.json")));
        var bad = WriteTemp("not json");
        Assert.Empty(ModConfigStore.Read(bad));
    }

    [Fact]
    public void Read_skips_nested_objects_and_arrays()
    {
        var path = WriteTemp("""{"A":true,"Nested":{"x":1},"Arr":[1,2],"B":"t"}""");
        var fields = ModConfigStore.Read(path);
        Assert.Equal(new[] { "A", "B" }, fields.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Write_updates_values_preserving_types_order_and_unmanaged_keys()
    {
        var path = WriteTemp("""
        {"HotReload":false,"LogLevel":"Information","BaseBgmId_P5R":12000,"Nested":{"x":1}}
        """);
        ModConfigStore.Write(path, new[]
        {
            new ModConfigField("HotReload", ConfigFieldKind.Bool, "true"),
            new ModConfigField("LogLevel", ConfigFieldKind.Text, "Debug"),
            new ModConfigField("BaseBgmId_P5R", ConfigFieldKind.Number, "13000"),
        });

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("HotReload").GetBoolean());                 // bool 維持
        Assert.Equal("Debug", root.GetProperty("LogLevel").GetString());          // 文字列 維持
        Assert.Equal(13000, root.GetProperty("BaseBgmId_P5R").GetInt32());        // 整数 維持
        Assert.Equal(1, root.GetProperty("Nested").GetProperty("x").GetInt32());  // 未対応キー 保持
        // キー順保持
        Assert.Equal(new[] { "HotReload", "LogLevel", "BaseBgmId_P5R", "Nested" },
            root.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Write_keeps_decimal_numbers_as_decimal()
    {
        var path = WriteTemp("""{"Volume":0.5}""");
        ModConfigStore.Write(path, new[] { new ModConfigField("Volume", ConfigFieldKind.Number, "0.8") });
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(0.8, doc.RootElement.GetProperty("Volume").GetDouble(), 3);
    }

    [Fact]
    public void Write_does_nothing_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        ModConfigStore.Write(path, new[] { new ModConfigField("A", ConfigFieldKind.Bool, "true") });
        Assert.False(File.Exists(path));
    }
}
