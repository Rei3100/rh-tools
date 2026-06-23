using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class PreferenceStoreTests
{
    [Fact]
    public void SetThenGet_ReturnsWinner_OrderInsensitive()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var store = new PreferenceStore(dir);
        store.SetWinner("p5r", "hairMod", "bodyMod", "hairMod");

        Assert.Equal("hairMod", store.GetWinner("p5r", "hairMod", "bodyMod"));
        Assert.Equal("hairMod", store.GetWinner("p5r", "bodyMod", "hairMod")); // 順不同
        Assert.Null(store.GetWinner("p5r", "hairMod", "other"));
    }

    [Fact]
    public void Persists_AcrossInstances()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        new PreferenceStore(dir).SetWinner("p5r", "a", "b", "b");
        Assert.Equal("b", new PreferenceStore(dir).GetWinner("p5r", "a", "b"));
    }
}
