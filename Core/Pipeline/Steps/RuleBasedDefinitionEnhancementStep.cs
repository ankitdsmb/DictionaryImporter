using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DictionaryImporter.Core.Pipeline.Steps
{
    public sealed class RuleBasedDefinitionEnhancementStep(
        IAiAnnotationRepository repository,
        IGrammarCorrector dictionaryRewriteCorrector,
        IRewriteContextAccessor rewriteContextAccessor,
        IOptions<RuleBasedRewriteDefinitionsOptions> options,
        ILogger<RuleBasedDefinitionEnhancementStep> logger)
        : IImportPipelineStep
    {
        public const string StepName = "RuleBasedRewrite";

        private const string RewriteEngineVersion = "DictionaryRewriteV1";

        private readonly IAiAnnotationRepository _repository = repository;
        private readonly IGrammarCorrector _dictionaryRewriteCorrector = dictionaryRewriteCorrector;
        private readonly IRewriteContextAccessor _rewriteContextAccessor = rewriteContextAccessor;
        private readonly RuleBasedRewriteDefinitionsOptions _options = options.Value ?? new RuleBasedRewriteDefinitionsOptions();
        private readonly ILogger<RuleBasedDefinitionEnhancementStep> _logger = logger;

        public string Name => StepName;

        public async Task ExecuteAsync(ImportPipelineContext context)
        {
            if (context is null)
                return;

            if (!_options.Enabled)
            {
                _logger.LogInformation("RuleBasedDefinitionEnhancementStep skipped (disabled by config).");
                return;
            }

            var sourceCode = NormalizeSource(context.SourceCode);

            // FIX: ImportPipelineContext does not contain Take. Use deterministic fallback.
            var take = _options.Take > 0
                ? _options.Take
                : 500;

            var maxExamples = _options.MaxExamplesPerParsedDefinition <= 0
                ? 10
                : _options.MaxExamplesPerParsedDefinition;

            var provider = string.IsNullOrWhiteSpace(_options.Provider) ? "RuleBased" : _options.Provider;
            var model = string.IsNullOrWhiteSpace(_options.Model) ? "Regex+RewriteMap+Humanizer" : _options.Model;

            _logger.LogInformation(
                "RuleBasedDefinitionEnhancementStep started. SourceCode={SourceCode}, Take={Take}, MaxExamples={MaxExamples}, ForceRewrite={ForceRewrite}",
                sourceCode,
                take,
                maxExamples,
                _options.ForceRewrite);

            IReadOnlyList<AiDefinitionCandidate> candidates;

            try
            {
                candidates = await _repository.GetDefinitionCandidatesAsync(sourceCode, take, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RuleBasedDefinitionEnhancementStep: failed to read candidates.");
                return;
            }

            if (candidates.Count == 0)
            {
                _logger.LogInformation("RuleBasedDefinitionEnhancementStep: no candidates found.");
                return;
            }

            var filteredCandidates = candidates;

            if (!_options.ForceRewrite)
            {
                IReadOnlySet<long> alreadyEnhanced;

                try
                {
                    var ids = candidates.Select(x => x.ParsedDefinitionId).ToList();

                    alreadyEnhanced = await _repository.GetAlreadyEnhancedParsedIdsAsync(
                        sourceCode,
                        ids,
                        provider,
                        model,
                        context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RuleBasedDefinitionEnhancementStep: failed to load already enhanced ids. Continuing without skip.");
                    alreadyEnhanced = new HashSet<long>();
                }

                filteredCandidates = candidates
                    .Where(c => !alreadyEnhanced.Contains(c.ParsedDefinitionId))
                    .ToList();

                if (filteredCandidates.Count == 0)
                {
                    _logger.LogInformation(
                        "RuleBasedDefinitionEnhancementStep: all candidates already enhanced. SourceCode={SourceCode}, Provider={Provider}, Model={Model}",
                        sourceCode,
                        provider,
                        model);

                    return;
                }
            }

            IReadOnlyDictionary<long, IReadOnlyList<string>> examplesLookup;

            try
            {
                var parsedIds = filteredCandidates.Select(x => x.ParsedDefinitionId).ToList();

                examplesLookup = await _repository.GetExamplesByParsedIdsAsync(
                    sourceCode,
                    parsedIds,
                    maxExamples,
                    context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RuleBasedDefinitionEnhancementStep: failed to load examples. Continuing without examples.");
                examplesLookup = new Dictionary<long, IReadOnlyList<string>>();
            }

            var enhancements = new List<AiDefinitionEnhancement>(filteredCandidates.Count);

            foreach (var c in filteredCandidates)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var originalDefinition = c.DefinitionText ?? string.Empty;
                    var originalTitle = c.MeaningTitle ?? string.Empty;

                    _rewriteContextAccessor.Current = new RewriteContext
                    {
                        SourceCode = sourceCode,
                        Mode = RewriteTargetMode.Definition
                    };

                    var rewrittenDefinition = await SafeRewriteAsync(originalDefinition, context.CancellationToken);

                    _rewriteContextAccessor.Current = new RewriteContext
                    {
                        SourceCode = sourceCode,
                        Mode = RewriteTargetMode.Title
                    };

                    var rewrittenTitle = string.IsNullOrWhiteSpace(originalTitle)
                        ? string.Empty
                        : await SafeRewriteAsync(originalTitle, context.CancellationToken);

                    var rewrittenExamples = new List<object>();
                    var totalExamples = 0;
                    var rewrittenExamplesCount = 0;

                    if (examplesLookup.TryGetValue(c.ParsedDefinitionId, out var exampleList) &&
                        exampleList is not null &&
                        exampleList.Count > 0)
                    {
                        totalExamples = exampleList.Count;

                        _rewriteContextAccessor.Current = new RewriteContext
                        {
                            SourceCode = sourceCode,
                            Mode = RewriteTargetMode.Example
                        };

                        foreach (var ex in exampleList)
                        {
                            if (context.CancellationToken.IsCancellationRequested)
                                break;

                            if (string.IsNullOrWhiteSpace(ex))
                                continue;

                            var exRewritten = await SafeRewriteAsync(ex, context.CancellationToken);

                            if (!string.Equals(ex.Trim(), exRewritten.Trim(), StringComparison.Ordinal))
                                rewrittenExamplesCount++;

                            rewrittenExamples.Add(new
                            {
                                original = ex,
                                rewritten = exRewritten
                            });
                        }
                    }

                    var notesJson = BuildNotesJson(
                        sourceCode: sourceCode,
                        provider: provider,
                        model: model,
                        forceRewrite: _options.ForceRewrite,
                        titleOriginal: originalTitle,
                        titleRewritten: rewrittenTitle,
                        examples: rewrittenExamples,
                        totalExamples: totalExamples,
                        rewrittenExamplesCount: rewrittenExamplesCount);

                    enhancements.Add(new AiDefinitionEnhancement
                    {
                        ParsedDefinitionId = c.ParsedDefinitionId,
                        OriginalDefinition = originalDefinition,
                        AiEnhancedDefinition = rewrittenDefinition,
                        AiNotesJson = notesJson,
                        Provider = provider,
                        Model = model
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "RuleBasedDefinitionEnhancementStep: candidate failed ParsedDefinitionId={Id}",
                        c.ParsedDefinitionId);
                }
            }

            try
            {
                await _repository.SaveAiEnhancementsAsync(sourceCode, enhancements, context.CancellationToken);

                _logger.LogInformation(
                    "RuleBasedDefinitionEnhancementStep completed. SourceCode={SourceCode}, Saved={Count}",
                    sourceCode,
                    enhancements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RuleBasedDefinitionEnhancementStep: failed to save enhancements.");
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
        private static string BuildNotesJson(
            string sourceCode,
            string provider,
            string model,
            bool forceRewrite,
            string titleOriginal,
            string titleRewritten,
            List<object> examples,
            int totalExamples,
            int rewrittenExamplesCount)
        {
            try
            {
                var payload = new
                {
                    audit = new
                    {
                        engine = RewriteEngineVersion,
                        step = StepName,
                        sourceCode = sourceCode,
                        provider = provider,
                        model = model,
                        forceRewrite = forceRewrite,
                        modes = new
                        {
                            definition = true,
                            title = true,
                            examples = true
                        },
                        counts = new
                        {
                            examplesTotal = totalExamples,
                            examplesRewritten = rewrittenExamplesCount
                        }
                    },
                    title = new
                    {
                        original = titleOriginal ?? string.Empty,
                        rewritten = titleRewritten ?? string.Empty
                    },
                    examples = examples ?? new List<object>()
                };

                return JsonSerializer.Serialize(payload);
            }
            catch
            {
                return "{}";
            }
        }

        // NEW METHOD (added)
        private static string NormalizeSource(string? sourceCode)
            => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();
    }
}
