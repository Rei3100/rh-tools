using System.IO;
using System.Text;

namespace ReloadedHelper.Core;

public static class UpdateReportLog
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ReloadedHelper", "update-log.txt");

    public static string Format(MetadataRefreshResult r)
    {
        var id = r.GbId is null ? "" : $" gb={r.GbId}";
        return $"[{r.Status}] {r.ModId}{id} — {r.Reason}";
    }

    public static void Append(string path, IEnumerable<MetadataRefreshResult> results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 更新 ===");
        foreach (var r in results) sb.AppendLine(Format(r));
        File.AppendAllText(path, sb.ToString());
    }
}
