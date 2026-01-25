namespace DictionaryImporter.Gateway.Rewriter
{
    public interface IRewriteMapCandidateRepository
    {
        Task UpsertCandidatesAsync(
            IReadOnlyList<RewriteMapCandidateUpsert> candidates,
            CancellationToken ct);

        Task<IReadOnlyList<RewriteMapCandidateRow>> GetApprovedCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct);

        Task MarkPromotedAsync(
            IReadOnlyList<long> candidateIds,
            string approvedBy,
            CancellationToken ct);

        // NEW METHOD (added)
        Task<IReadOnlySet<string>> GetExistingRewriteMapKeysAsync(
            string sourceCode,
            CancellationToken ct);
    }

    public sealed class RewriteMapCandidateUpsert
    {
        public string SourceCode { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public string FromText { get; init; } = string.Empty;
        public string ToText { get; init; } = string.Empty;
        public decimal Confidence { get; init; } = 0;
    }

    public sealed class RewriteMapCandidateRow
    {
        public long RewriteMapCandidateId { get; init; }
        public string SourceCode { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public string FromText { get; init; } = string.Empty;
        public string ToText { get; init; } = string.Empty;
        public int SuggestedCount { get; init; }
        public decimal AvgConfidenceScore { get; init; }
    }
}