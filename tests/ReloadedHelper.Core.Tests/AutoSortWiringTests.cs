using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class AutoSortWiringTests
{
    [Fact]
    public void BuildTypeDecisions_GivesTypeAndReason()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = new("lib", "Lib", "", "1", "", System.Array.Empty<string>(), System.Array.Empty<string>(),
                System.Array.Empty<string>(), System.Array.Empty<string>(), null, null, null, null, "", IsLibrary: true),
        };
        var entries = new[] { new ModLoadEntry(0, "lib", catalog["lib"], true, null, true) };

        var decisions = MainViewModel.BuildTypeDecisions(entries, catalog);

        Assert.Equal(ModType.Library, decisions["lib"].Type);
        Assert.False(string.IsNullOrWhiteSpace(decisions["lib"].Reason));
    }

    [Fact]
    public void BuildTypeDecisions_NoInfo_IsUnknown()
    {
        var catalog = new Dictionary<string, ModInfo>();
        var entries = new[] { new ModLoadEntry(0, "ghost", null, true, null, false) };

        var decisions = MainViewModel.BuildTypeDecisions(entries, catalog);

        Assert.Equal(ModType.Unknown, decisions["ghost"].Type);
    }
}
