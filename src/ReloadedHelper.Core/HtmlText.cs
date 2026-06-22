using System.Text.RegularExpressions;

namespace ReloadedHelper.Core;

public static class HtmlText
{
    public static string Strip(string html)
    {
        if (string.IsNullOrEmpty(html)) return html ?? "";
        var s = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
             .Replace("&quot;", "\"").Replace("&#39;", "'");
        return s.Trim();
    }
}
