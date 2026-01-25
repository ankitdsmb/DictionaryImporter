using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Core.Jobs
{
    public sealed class RuleBasedRewriteJob(
        IAiAnnotationRepository repository,
        IGrammarCorrector dictionaryRewriteCorrector,
        IRewriteContextAccessor rewriteContextAccessor,
        ILogger<RuleBasedRewriteJob> logger,
        IOptions<RuleBasedRewriteJobOptions> options)
    {
        private readonly IAiAnnotationRepository _repository = repository;
        private readonly IGrammarCorrector _dictionaryRewriteCorrector = dictionaryRewriteCorrector;
        private readonly IRewriteContextAccessor _rewriteContextAccessor = rewriteContextAccessor;
        private readonly ILogger<RuleBasedRewriteJob> _logger = logger;
        private readonly RuleBasedRewriteJobOptions _options = options.Value;

        // ✅ Program.cs expects RunAsync()
        public Task RunAsync(CancellationToken ct = default)
            => ExecuteAsync(ct);

        public async Task ExecuteAsync(CancellationToken ct)
        {
            var sourceCode = "UNKNOWN"; // ✅ Options does not include SourceCode in your codebase
            var take = _options.Take <= 0 ? 500 : _options.Take;

            _logger.LogInformation(
                "RuleBasedRewriteJob started. SourceCode={SourceCode}, Take={Take}",
                sourceCode,
                take);

            IReadOnlyList<AiDefinitionCandidate> candidates;

            try
            {
                candidates = await _repository.GetDefinitionCandidatesAsync(sourceCode, take, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RuleBasedRewriteJob: failed to read candidates.");
                return;
            }

            if (candidates.Count == 0)
            {
                _logger.LogInformation("RuleBasedRewriteJob: no candidates found.");
                return;
            }

            var enhancements = new List<AiDefinitionEnhancement>(candidates.Count);

            foreach (var c in candidates)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var original = c.DefinitionText ?? string.Empty;

                    // ✅ Context for RewriteMap engine (Source + Mode)
                    _rewriteContextAccessor.Current = new RewriteContext
                    {
                        SourceCode = sourceCode,
                        Mode = RewriteTargetMode.Definition
                    };

                    var rewritten = await SafeRewriteAsync(original, ct);

                    enhancements.Add(new AiDefinitionEnhancement
                    {
                        ParsedDefinitionId = c.ParsedDefinitionId,
                        OriginalDefinition = original,
                        AiEnhancedDefinition = rewritten,
                        AiNotesJson = "{}", // deterministic, no hallucinations
                        Provider = "RuleBased",
                        Model = "Regex+RewriteMap+Humanizer"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "RuleBasedRewriteJob: candidate processing failed ParsedDefinitionId={Id}",
                        c.ParsedDefinitionId);
                }
            }

            try
            {
                await _repository.SaveAiEnhancementsAsync(sourceCode, enhancements, ct);
                _logger.LogInformation(
                    "RuleBasedRewriteJob completed. Saved={Count}",
                    enhancements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RuleBasedRewriteJob: failed to save enhancements.");
            }
        }

        // NEW METHOD (added)
        private async Task<string> SafeRewriteAsync(string text, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return text;

                // Uses DictionaryRewriteCorrectorAdapter (regex JSON + RewriteMap + Humanizer)
                var result = await _dictionaryRewriteCorrector.AutoCorrectAsync(text, "en", ct);

                var output = result?.CorrectedText ?? text;

                if (string.IsNullOrWhiteSpace(output))
                    return text;

                return output.Trim();
            }
            catch
            {
                return text; // ✅ never crash
            }
        }
    }
}
