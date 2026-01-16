namespace DictionaryImporter.Core.Pipeline.Steps;

public sealed class IpaPipelineStep(
    CanonicalWordIpaEnricher ipaEnricher,
    IReadOnlyList<IpaSourceConfig> ipaSources) : IImportPipelineStep
{
    public string Name => PipelineStepNames.Ipa;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        foreach (var ipa in ipaSources)
        {
            await ipaEnricher.ExecuteAsync(
                ipa.Locale,
                ipa.FilePath,
                context.CancellationToken);
        }
    }
}