using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class AutoSortWiringTests
{
    [Fact]
    public void BuildRoles_UsesCategoryAndLibraryFlag()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["skin"] = new("skin", "Skin", "", "1", "", Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, ""),
            ["lib"] = new("lib", "Lib", "", "1", "", Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, "", IsLibrary: true),
        };
        var entries = new[]
        {
            new ModLoadEntry(0, "skin", catalog["skin"], true, "Skin", false),
            new ModLoadEntry(1, "lib", catalog["lib"], true, null, true),
        };

        var roles = MainViewModel.BuildRoles(entries, catalog);

        Assert.Equal(ModRole.VisualOverride, roles["skin"]);
        Assert.Equal(ModRole.Library, roles["lib"]);
    }

    [Fact]
    public void BuildRoleDecisions_GivesRoleAndReason()
    {
        var catalog = new Dictionary<string, ModInfo>
        {
            ["lib"] = new("lib", "Lib", "", "1", "", Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), null, null, null, null, "", IsLibrary: true),
        };
        var entries = new[] { new ModLoadEntry(0, "lib", catalog["lib"], true, null, true) };

        var decisions = MainViewModel.BuildRoleDecisions(entries, catalog);

        Assert.Equal(ModRole.Library, decisions["lib"].Role);
        Assert.False(string.IsNullOrWhiteSpace(decisions["lib"].Reason));
    }
}
