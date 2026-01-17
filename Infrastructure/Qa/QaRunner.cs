namespace DictionaryImporter.Infrastructure.Qa
{
    public sealed class QaRunner(
        IEnumerable<IQaCheck> checks,
        ILogger<QaRunner> logger)
    {
        public async Task<IReadOnlyList<QaSummaryRow>> RunAsync(
            CancellationToken ct)
        {
            var results = new List<QaSummaryRow>();

            foreach (var check in checks.OrderBy(c => c.Phase))
            {
                ct.ThrowIfCancellationRequested();

                logger.LogInformation(
                    "QA started | Phase={Phase} | Check={Check}",
                    check.Phase,
                    check.Name);

                var rows = await check.ExecuteAsync(ct);

                foreach (var r in rows)
                    logger.LogInformation(
                        "QA result | Phase={Phase} | Check={Check} | Status={Status} | {Details}",
                        r.Phase,
                        r.CheckName,
                        r.Status,
                        r.Details);

                results.AddRange(rows);
            }

            return results;
        }
    }
}