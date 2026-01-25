namespace DictionaryImporter.Core.Rewrite;

public interface IRewriteContextAccessor
{
    RewriteContext Current { get; set; }
}