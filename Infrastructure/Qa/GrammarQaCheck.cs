using DictionaryImporter.AITextKit.Grammar.Core;

namespace DictionaryImporter.Infrastructure.Qa;

public sealed class GrammarQaCheck(string connectionString, IGrammarCorrector grammarCorrector, ILogger<GrammarQaCheck> logger) : IQaCheck
{
    public string Name => "grammar-consistency";
    public string Phase => "post-processing";

    public async Task<IReadOnlyList<QaSummaryRow>> ExecuteAsync(CancellationToken ct)
    {
        var results = new List<QaSummaryRow>();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var definitions = await conn.QueryAsync<string>(
            """
            SELECT TOP 100 Definition
            FROM dbo.DictionaryEntry
            WHERE LEN(Definition) > 50
            ORDER BY NEWID()
            """);

        var checkedCount = 0;
        var issueCount = 0;

        foreach (var definition in definitions)
        {
            ct.ThrowIfCancellationRequested();

            var checkResult = await grammarCorrector.CheckAsync(definition, "en-US", ct);
            if (checkResult.HasIssues)
            {
                issueCount += checkResult.IssueCount;

                var severeIssues = checkResult.Issues
                    .Where(i => i.ConfidenceLevel > 90)
                    .ToList();

                if (severeIssues.Any())
                {
                    logger.LogDebug(
                        "Grammar issues in definition: {IssueCount} issues, {SevereCount} severe",
                        checkResult.IssueCount,
                        severeIssues.Count
                    );
                }
            }

            checkedCount++;
        }

        var status = issueCount == 0 ? "PASS" : (issueCount < 10 ? "WARN" : "FAIL");
        results.Add(new QaSummaryRow
        {
            Phase = Phase,
            CheckName = Name,
            Status = status,
            Details = $"Checked {checkedCount} definitions, found {issueCount} grammar issues"
        });

        return results;
    }
}