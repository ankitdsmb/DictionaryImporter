namespace DictionaryImporter.Core.Rewrite;

public sealed class RewriteContext
{
    public string SourceCode { get; set; } = "UNKNOWN";

    public RewriteTargetMode Mode { get; set; } = RewriteTargetMode.Definition;
}