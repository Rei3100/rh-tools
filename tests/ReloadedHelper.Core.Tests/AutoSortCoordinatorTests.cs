using System.IO;
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class AutoSortCoordinatorTests
{
    [Fact]
    public void Apply_BacksUpThenApplies_AndRecordsHistory()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var coord = new AutoSortCoordinator(dir);
        var calls = new List<string>();
        var result = new OptimizeResult(
            new[] { "a", "b" },
            new[] { new PlacementReason("a", "b", "msg") },
            Array.Empty<(string, string)>(),
            Array.Empty<ModPlacement>());

        var entry = coord.Apply(AutoSortTrigger.ToggleEnable, result,
            applyOrder: o => calls.Add("apply:" + string.Join(",", o)),
            backup: () => calls.Add("backup"));

        Assert.Equal(new[] { "backup", "apply:a,b" }, calls); // バックアップが先
        Assert.Single(entry.Reasons);
        Assert.Single(coord.History);
    }

    [Fact]
    public void History_Persists_NewestFirst()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var empty = new OptimizeResult(Array.Empty<string>(),
            Array.Empty<PlacementReason>(), Array.Empty<(string, string)>(),
            Array.Empty<ModPlacement>());
        var c1 = new AutoSortCoordinator(dir);
        c1.Apply(AutoSortTrigger.Startup, empty, _ => { }, () => { });

        var c2 = new AutoSortCoordinator(dir); // 再読み込み
        Assert.Single(c2.History);
        Assert.Equal(AutoSortTrigger.Startup, c2.History[0].Trigger);
    }
}
