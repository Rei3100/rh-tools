namespace ReloadedHelper.Core.Analyzers;

public enum ResourceKind { File, Song, Costume }

public readonly record struct ResourceKey(ResourceKind Kind, string Value)
{
    private string Prefix => Kind switch
    {
        ResourceKind.File => "file",
        ResourceKind.Song => "song",
        ResourceKind.Costume => "costume",
        _ => "unknown",
    };

    public override string ToString() => $"{Prefix}:{Value}";
}

public sealed record ModResources(string ModId, IReadOnlyList<ResourceKey> Resources);

public interface IModAnalyzer
{
    // 解析できなければ空リストを返す（例外で握りつぶさない）。
    IReadOnlyList<ResourceKey> Analyze(ModInfo mod);
}
