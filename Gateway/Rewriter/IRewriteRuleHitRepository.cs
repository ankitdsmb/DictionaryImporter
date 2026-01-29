namespace DictionaryImporter.Gateway.Rewriter;

public interface IRewriteRuleHitRepository
{
    Task UpsertHitsAsync(
        IReadOnlyList<RewriteRuleHitUpsert> hits,
        CancellationToken ct);
}