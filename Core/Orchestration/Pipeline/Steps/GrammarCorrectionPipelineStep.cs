using DictionaryImporter.Core.Orchestration.Models;
using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Feature;

namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class GrammarCorrectionPipelineStep(
    IGrammarFeature grammar,
    IRewriteContextAccessor rewriteContextAccessor) : IImportPipelineStep
{
    private readonly IGrammarFeature _grammar = grammar;
    private readonly IRewriteContextAccessor _rewriteContextAccessor = rewriteContextAccessor;

    public string Name => PipelineStepNames.GrammarCorrection;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        if (context is null)
            return;

        var source = string.IsNullOrWhiteSpace(context.SourceCode) ? "UNKNOWN" : context.SourceCode.Trim();

        RewriteContext? previous = null;

        try
        {
            previous = _rewriteContextAccessor.Current;

            _rewriteContextAccessor.Current = new RewriteContext
            {
                SourceCode = source,
                Mode = RewriteTargetMode.Definition
            };

            await _grammar.CorrectSourceAsync(source, context.CancellationToken);
        }
        finally
        {
            // Restore previous context (AsyncLocal hygiene)
            _rewriteContextAccessor.Current = previous ?? new RewriteContext
            {
                SourceCode = source,
                Mode = RewriteTargetMode.Definition
            };
        }
    }
}