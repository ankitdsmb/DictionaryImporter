using System.Threading;

namespace DictionaryImporter.Core.Rewrite;

public sealed class RewriteContextAccessor : IRewriteContextAccessor
{
    private static readonly AsyncLocal<RewriteContext?> Local = new();

    public RewriteContext Current
    {
        get => Local.Value ??= new RewriteContext();
        set => Local.Value = value ?? new RewriteContext();
    }
}