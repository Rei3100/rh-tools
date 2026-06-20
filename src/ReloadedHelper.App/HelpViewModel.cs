using System.Reflection;

namespace ReloadedHelper.App;

public sealed class HelpViewModel
{
    public string VersionString =>
        "バージョン " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");
}
