using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class PlacementHintParserTests
{
    private static ModInfo Mod(string desc) => new(
        "id", "name", "", "1", desc,
        System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>(),
        new[] { "p5r" }, null, null, null, null, "");

    [Fact]
    public void Late_WhenDescriptionSaysLoadBelow()
        => Assert.Equal(PlacementHint.Late, PlacementHintParser.Parse(Mod("Make sure to load this below other mods.")));

    [Fact]
    public void Late_WhenJapaneseSaysBottom()
        => Assert.Equal(PlacementHint.Late, PlacementHintParser.Parse(Mod("このMODは一番下に置いてください。")));

    [Fact]
    public void Early_WhenDescriptionSaysLoadAbove()
        => Assert.Equal(PlacementHint.Early, PlacementHintParser.Parse(Mod("Load this above everything else.")));

    [Fact]
    public void None_ForNeutralDescription()
        => Assert.Equal(PlacementHint.None, PlacementHintParser.Parse(Mod("A retexture of Futaba.")));

    [Fact]
    public void None_WhenBothDirectionsPresent()
        => Assert.Equal(PlacementHint.None, PlacementHintParser.Parse(Mod("Load above A but below B.")));

    [Fact]
    public void None_ForEmpty()
        => Assert.Equal(PlacementHint.None, PlacementHintParser.Parse(Mod("")));
}
