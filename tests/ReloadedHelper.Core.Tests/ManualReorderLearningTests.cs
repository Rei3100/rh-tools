using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ManualReorderLearningTests
{
    [Fact]
    public void LearnFromManualOrder_LaterModIsWinner()
    {
        var conflicts = new[] { new FileConflict("file:hair", new[] { "a", "b" }, "b") };
        // ユーザーが手で a を後ろにした → a を勝たせたい
        var learned = MainViewModel.LearnFromManualOrder(new[] { "b", "a" }, conflicts);

        Assert.Contains(("a", "b"), learned); // (Winner=a, Loser=b)
    }

    [Fact]
    public void LearnFromManualOrder_IgnoresNonConflicting()
    {
        var learned = MainViewModel.LearnFromManualOrder(
            new[] { "x", "y" }, Array.Empty<FileConflict>());
        Assert.Empty(learned);
    }
}
