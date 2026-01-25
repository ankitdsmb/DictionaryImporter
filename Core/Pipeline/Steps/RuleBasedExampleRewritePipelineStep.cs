using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DictionaryImporter.Core.Pipeline.Steps
{
    public sealed class RuleBasedExampleRewritePipelineStep(
        IExampleAiEnhancementRepository repository,
        IGrammarCorrector dictionaryRewriteCorrector,
        IRewriteContextAccessor rewriteContextAccessor,
        IOptions<RuleBasedRewriteExamplesOptions> options,
        ILogger<RuleBasedExampleRewritePipelineStep> logger)
        : IImportPipelineStep
    {
        public const string StepName = "RuleBasedRewriteExamples";

        private readonly IExampleAiEnhancementRepository _repository = repository;
        private readonly IGrammarCorrector _dictionaryRewriteCorrector = dictionaryRewriteCorrector;
        private readonly IRewriteContextAccessor _rewriteContextAccessor = rewriteContextAccessor;
        private readonly RuleBasedRewriteExamplesOptions _options = options.Value ?? new RuleBasedRewriteExamplesOptions();
        private readonly ILogger<RuleBasedExampleRewritePipelineStep> _logger = logger;

        public string Name => StepName;

        public async Task ExecuteAsync(ImportPipelineContext context)
        {
            if (context is null)
                return;

            if (!_options.Enabled)
            {
                _logger.LogInformation("RuleBasedExampleRewritePipelineStep skipped (disabled by config).");
                return;
            }

            var sourceCode = NormalizeSource(context.SourceCode);

            // FIX: ImportPipelineContext does not contain Take. Use deterministic fallback.
            var take = _options.Take > 0
                ? _options.Take
                : 1000;

            _logger.LogInformation(
                "RuleBasedExampleRewritePipelineStep started. SourceCode={SourceCode}, Take={Take}, ForceRewrite={ForceRewrite}",
                sourceCode,
                take,
                _options.ForceRewrite);

            IReadOnlyList<ExampleRewriteCandidate> candidates;

            try
            {
                candidates = await _repository.GetExampleCandidatesAsync(
                    sourceCode,
                    take,
                    _options.ForceRewrite,
                    context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RuleBasedExampleRewritePipelineStep: failed to read candidates.");
                return;
            }

            if (candidates.Count == 0)
            {
                _logger.LogInformation("RuleBasedExampleRewritePipelineStep: no candidates found.");
                return;
            }

            var updates = new List<ExampleRewriteEnhancement>(candidates.Count);

            foreach (var c in candidates)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var original = c.ExampleText ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(original))
                        continue;

                    _rewriteContextAccessor.Current = new RewriteContext
                    {
                        SourceCode = sourceCode,
                        Mode = RewriteTargetMode.Example
                    };

                    var rewritten = await SafeRewriteAsync(original, context.CancellationToken);

                    if (string.IsNullOrWhiteSpace(rewritten))
                        continue;

                    if (string.Equals(original.Trim(), rewritten.Trim(), StringComparison.Ordinal))
                        continue;

                    updates.Add(new ExampleRewriteEnhancement
                    {
                        DictionaryEntryExampleId = c.DictionaryEntryExampleId,
                        OriginalExampleText = original,
                        RewrittenExampleText = rewritten,
                        Model = string.IsNullOrWhiteSpace(_options.Model) ? "Regex+RewriteMap+Humanizer" : _options.Model,
                        Confidence = _options.ConfidenceScore <= 0 ? 100 : _options.ConfidenceScore
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "RuleBasedExampleRewritePipelineStep: candidate failed ExampleId={Id}",
                        c.DictionaryEntryExampleId);
                }
            }

            try
            {
                await _repository.SaveExampleEnhancementsAsync(
                    sourceCode,
                    updates,
                    _options.ForceRewrite,
                    context.CancellationToken);

                _logger.LogInformation(
                    "RuleBasedExampleRewritePipelineStep completed. SourceCode={SourceCode}, Updated={Count}",
                    sourceCode,
                    updates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RuleBasedExampleRewritePipelineStep: failed to save updates.");
            }
        }

        // NEW METHOD (added)
        private async Task<string> SafeRewriteAsync(string text, System.Threading.CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return text;

                var result = await _dictionaryRewriteCorrector.AutoCorrectAsync(text, "en", ct);
                var output = result?.CorrectedText ?? text;

                if (string.IsNullOrWhiteSpace(output))
                    return text;

                return output.Trim();
            }
            catch
            {
                return text;
            }
        }

        // NEW METHOD (added)
        private static string NormalizeSource(string? sourceCode)
            => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();
    }
}
