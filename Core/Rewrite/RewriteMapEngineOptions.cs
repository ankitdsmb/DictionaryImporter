namespace DictionaryImporter.Core.Rewrite;

public sealed class RewriteMapEngineOptions
{
    public int CacheTtlSeconds { get; set; } = 600; // default 10 min

    public int MaxAppliedCorrections { get; set; } = 200;

    public int RegexTimeoutMs { get; set; } = 60;

    public bool EnableCaching { get; set; } = true;
}