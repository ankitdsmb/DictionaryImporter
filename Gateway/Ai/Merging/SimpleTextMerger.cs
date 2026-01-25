using DictionaryImporter.Gateway.Ai.Abstractions;

namespace DictionaryImporter.Gateway.Ai.Merging;

public sealed class SimpleTextMerger : IAiResultMerger
{
    public AiProviderResult Merge(AiGatewayRequest request, List<AiProviderResult> results)
    {
        var ok = results
            .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.Text))
            .ToList();

        if (ok.Count == 0)
        {
            return new AiProviderResult
            {
                Provider = "MergeEngine",
                Model = "none",
                Success = false,
                Error = "No successful provider results to merge."
            };
        }

        // Choose "best" by simplest heuristic: longer cleaned text wins
        var winner = ok
            .OrderByDescending(x => x.Text!.Trim().Length)
            .First();

        return new AiProviderResult
        {
            Provider = winner.Provider,
            Model = winner.Model,
            Success = true,
            Text = winner.Text
        };
    }
}