namespace ReloadedHelper.Core.Tests;

public class AppConfigWriterRemoveModTests : IDisposable
{
    private readonly string _tmp = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_tmp);
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReloadedHelper", "backups", "remove-test");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void RemoveMod_RemovesFromBothLists_PreservesOthers()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "remove-test",
              "AppName": "TestGame",
              "EnabledMods": ["ModA", "ModB", "ModC"],
              "SortedMods":  ["ModA", "ModB", "ModC"]
            }
            """);

        AppConfigWriter.RemoveMod(_tmp, "remove-test", "ModB");

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModA", "ModC" }, result.EnabledMods);
        Assert.Equal(new[] { "ModA", "ModC" }, result.SortedMods);
        Assert.Equal("TestGame", result.AppName);
    }

    [Fact]
    public void RemoveMod_CaseInsensitive()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "remove-test",
              "EnabledMods": ["ModA", "MODB"],
              "SortedMods":  ["ModA", "MODB"]
            }
            """);

        AppConfigWriter.RemoveMod(_tmp, "remove-test", "modb");

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModA" }, result.EnabledMods);
        Assert.Equal(new[] { "ModA" }, result.SortedMods);
    }

    [Fact]
    public void RemoveMod_WhenModNotPresent_NoChange()
    {
        File.WriteAllText(_tmp, """
            {
              "AppId": "remove-test",
              "EnabledMods": ["ModA"],
              "SortedMods":  ["ModA"]
            }
            """);

        AppConfigWriter.RemoveMod(_tmp, "remove-test", "NonExistent");

        var result = AppConfigParser.Parse(File.ReadAllText(_tmp), Path.GetTempPath());
        Assert.Equal(new[] { "ModA" }, result.EnabledMods);
        Assert.Equal(new[] { "ModA" }, result.SortedMods);
    }
}
