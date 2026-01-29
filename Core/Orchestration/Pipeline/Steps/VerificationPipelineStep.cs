using DictionaryImporter.Core.Orchestration.Models;

namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class VerificationPipelineStep(
    IPostMergeVerifier postMergeVerifier,
    IpaVerificationReporter ipaVerificationReporter) : IImportPipelineStep
{
    public string Name => PipelineStepNames.Verification;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await postMergeVerifier.VerifyAsync(context.SourceCode, context.CancellationToken);
        await ipaVerificationReporter.ReportAsync(context.CancellationToken);
    }
}