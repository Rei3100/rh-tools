using System.Text;
using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public static class ConfigLabel
{
    public static string Humanize(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        // アンダースコアを空白に
        var s = key.Replace('_', ' ');
        // camelCase / PascalCase / 数字境界で空白挿入
        s = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
        s = Regex.Replace(s, "([a-z])([0-9])", "$1 $2"); // 小文字→数字のみ分割（P5R 等の型番は割らない）
        // 連続空白を1つに、各語を先頭大文字化
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            var w = words[i];
            sb.Append(char.ToUpperInvariant(w[0]));
            sb.Append(w.Length > 1 ? w[1..] : "");
        }
        return sb.ToString();
    }
}
