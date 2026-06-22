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
}
