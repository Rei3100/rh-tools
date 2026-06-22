namespace ReloadedHelper.Core.Tests;

public class ConfigLabelTests
{
    [Theory]
    [InlineData("DisableVictoryBgm", "Disable Victory Bgm")]
    [InlineData("HotReload", "Hot Reload")]
    [InlineData("BaseBgmId_P5R", "Base Bgm Id P5R")]
    [InlineData("log_level", "Log Level")]
    [InlineData("Volume", "Volume")]
    [InlineData("", "")]
    public void Humanize_splits_words(string key, string expected)
    {
        Assert.Equal(expected, ConfigLabel.Humanize(key));
    }
}
