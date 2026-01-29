using DictionaryImporter.Core.Orchestration.Models;

namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class OrthographicSyllablesPipelineStep(
    CanonicalWordOrthographicSyllableEnricher orthographicSyllableEnricher,
    IReadOnlyList<IpaSourceConfig> ipaSources) : IImportPipelineStep
{
    public string Name => PipelineStepNames.OrthographicSyllables;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        foreach (var ipa in ipaSources)
        {
            await orthographicSyllableEnricher.ExecuteAsync(ipa.Locale, context.CancellationToken);
        }
    }
}