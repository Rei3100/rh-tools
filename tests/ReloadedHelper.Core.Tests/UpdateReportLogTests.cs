using System.IO;
namespace ReloadedHelper.Core.Tests;

public class UpdateReportLogTests
{
    [Fact]
    public void Format_includes_modid_status_reason()
    {
        var r = new MetadataRefreshResult("m1", RefreshStatus.GbMatched, "491359",
            "名前", "説明", "Skin", "author", "GB一致");
        var line = UpdateReportLog.Format(r);
        Assert.Contains("m1", line);
        Assert.Contains("GB一致", line);
        Assert.Contains("491359", line);
    }

    [Fact]
    public void Append_writes_lines_to_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"log-{Guid.NewGuid():N}.txt");
        try
        {
            UpdateReportLog.Append(path, new[]
            {
                new MetadataRefreshResult("m1", RefreshStatus.GbMatched, "1", "", "", null, null, "GB一致"),
                new MetadataRefreshResult("m2", RefreshStatus.Failed, null, "", "", null, null, "GB取得失敗"),
            });
            var text = File.ReadAllText(path);
            Assert.Contains("m1", text);
            Assert.Contains("m2", text);
            Assert.Contains("GB取得失敗", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
