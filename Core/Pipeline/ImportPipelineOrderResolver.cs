namespace DictionaryImporter.Core.Pipeline;

public sealed class ImportPipelineOrderResolver(IOptions<ImportPipelineOptions> options)
{
    private readonly ImportPipelineOptions _options = options.Value ?? new ImportPipelineOptions();

    public IReadOnlyList<string> Resolve(string sourceCode)
    {
        if (!string.IsNullOrWhiteSpace(sourceCode) &&
            _options.Sources.TryGetValue(sourceCode, out var src) &&
            src?.Steps?.Count > 0)
        {
            return src.Steps;
        }

        return _options.DefaultSteps;
    }
}