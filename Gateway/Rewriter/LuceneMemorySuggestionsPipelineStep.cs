namespace DictionaryImporter.Gateway.Rewriter
{
    public sealed class LuceneMemorySuggestionsPipelineStep(
        IAiAnnotationRepository repository,
        ILuceneSuggestionEngine suggestionEngine,
        LuceneIndexBuilder indexBuilder,
        IRewriteMapCandidateRepository candidateRepository,
        IOptions<LuceneSuggestionsOptions> options,
        ILogger<LuceneMemorySuggestionsPipelineStep> logger)
        : IImportPipelineStep
    {
        public const string StepName = "LuceneMemorySuggestions";

        private readonly IAiAnnotationRepository _repository = repository;
        private readonly ILuceneSuggestionEngine _suggestionEngine = suggestionEngine;
        private readonly LuceneIndexBuilder _indexBuilder = indexBuilder;
        private readonly IRewriteMapCandidateRepository _candidateRepository = candidateRepository;
        private readonly LuceneSuggestionsOptions _options = options.Value ?? new LuceneSuggestionsOptions();
        private readonly ILogger<LuceneMemorySuggestionsPipelineStep> _logger = logger;

        public string Name => StepName;

        public async Task ExecuteAsync(ImportPipelineContext context)
        {
            if (context is null)
                return;

            if (!_options.Enabled)
            {
                _logger.LogInformation("LuceneMemorySuggestionsPipelineStep skipped (disabled by config).");
                return;
            }

            var sourceCode = NormalizeSource(context.SourceCode);

            var indexPath = string.IsNullOrWhiteSpace(_options.IndexPath)
                ? "indexes/lucene/dictionary-rewrite-memory"
                : _options.IndexPath.Trim();

            var maxSuggestions = _options.MaxSuggestions <= 0 ? 3 : _options.MaxSuggestions;
            if (maxSuggestions > 10) maxSuggestions = 10;

            var minScore = _options.MinScore <= 0 ? 1.2 : _options.MinScore;

            var take = _options.Take <= 0 ? 500 : _options.Take;
            if (take > 5000) take = 5000;

            var writeCandidates = _options.WriteCandidatesToSql;
            var candidateMinConfidence = _options.CandidateMinConfidence <= 0 ? 0.75m : _options.CandidateMinConfidence;
            if (candidateMinConfidence > 1) candidateMinConfidence = 1;

            var maxCandidatesPerRun = _options.MaxCandidatesPerRun <= 0 ? 300 : _options.MaxCandidatesPerRun;
            if (maxCandidatesPerRun > 5000) maxCandidatesPerRun = 5000;

            const string provider = "RuleBased";
            const string model = "Regex+RewriteMap+Humanizer";

            _logger.LogInformation(
                "LuceneMemorySuggestionsPipelineStep started. SourceCode={SourceCode}, Take={Take}, IndexPath={IndexPath}, MaxSuggestions={MaxSuggestions}, MinScore={MinScore}, WriteCandidates={WriteCandidates}",
                sourceCode,
                take,
                indexPath,
                maxSuggestions,
                minScore,
                writeCandidates);

            // 1) Ensure index exists (build only if missing/empty)
            try
            {
                if (!Directory.Exists(indexPath) || DirectoryIsEmpty(indexPath))
                {
                    _logger.LogInformation(
                        "Lucene index missing/empty. Building index. SourceCode={SourceCode}, IndexPath={IndexPath}",
                        sourceCode,
                        indexPath);

                    await _indexBuilder.BuildOrUpdateIndexAsync(indexPath, sourceCode, context.CancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LuceneMemorySuggestionsPipelineStep: failed to ensure Lucene index. Continuing without suggestions.");
                return;
            }

            // 2) Load candidates
            IReadOnlyList<AiDefinitionCandidate> candidates;
            try
            {
                candidates = await _repository.GetDefinitionCandidatesAsync(sourceCode, take, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LuceneMemorySuggestionsPipelineStep: failed to read candidates.");
                return;
            }

            if (candidates.Count == 0)
            {
                _logger.LogInformation("LuceneMemorySuggestionsPipelineStep: no candidates found.");
                return;
            }

            var parsedIds = candidates
                .Select(x => x.ParsedDefinitionId)
                .Distinct()
                .ToList();

            var updatedNotesCount = 0;
            var candidateUpserts = new List<RewriteMapCandidateUpsert>();

            foreach (var parsedId in parsedIds)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var originalDefinition = await _repository.GetOriginalDefinitionAsync(
                        sourceCode,
                        parsedId,
                        context.CancellationToken);

                    if (string.IsNullOrWhiteSpace(originalDefinition))
                        continue;

                    var currentNotes = await _repository.GetAiNotesJsonAsync(
                        sourceCode,
                        parsedId,
                        provider,
                        model,
                        context.CancellationToken);

                    // Collect suggestions per mode with explicit FromText
                    var suggestionPairs = new List<(string FromText, LuceneSuggestionResult Suggestion)>();

                    // Definition suggestions
                    {
                        var defSuggestions = await _suggestionEngine.GetSuggestionsAsync(
                            sourceCode,
                            LuceneSuggestionMode.Definition,
                            originalDefinition,
                            maxSuggestions,
                            minScore,
                            context.CancellationToken);

                        foreach (var s in defSuggestions)
                            suggestionPairs.Add((originalDefinition, s));
                    }

                    // Title suggestions
                    {
                        var (titleOriginal, _) = AiNotesJsonReader.TryReadTitle(currentNotes);

                        if (!string.IsNullOrWhiteSpace(titleOriginal))
                        {
                            var titleSuggestions = await _suggestionEngine.GetSuggestionsAsync(
                                sourceCode,
                                LuceneSuggestionMode.MeaningTitle,
                                titleOriginal,
                                maxSuggestions,
                                minScore,
                                context.CancellationToken);

                            foreach (var s in titleSuggestions)
                                suggestionPairs.Add((titleOriginal, s));
                        }
                    }

                    // Example suggestions (max 10)
                    {
                        var examples = AiNotesJsonReader.TryReadExamples(currentNotes, maxExamples: 10);

                        foreach (var ex in examples)
                        {
                            if (context.CancellationToken.IsCancellationRequested)
                                break;

                            if (string.IsNullOrWhiteSpace(ex.Original))
                                continue;

                            var exampleSuggestions = await _suggestionEngine.GetSuggestionsAsync(
                                sourceCode,
                                LuceneSuggestionMode.Example,
                                ex.Original,
                                maxSuggestions,
                                minScore,
                                context.CancellationToken);

                            foreach (var s in exampleSuggestions)
                                suggestionPairs.Add((ex.Original, s));
                        }
                    }

                    if (suggestionPairs.Count == 0)
                        continue;

                    // Update AiNotesJson with luceneSuggestions[] (keep deterministic top N)
                    var finalSuggestions = suggestionPairs
                        .Select(x => x.Suggestion)
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.MatchedHash, StringComparer.Ordinal)
                        .Take(maxSuggestions)
                        .ToList();

                    var updatedNotes = LuceneMemorySuggestionJsonWriter.UpsertLuceneSuggestions(currentNotes, finalSuggestions);

                    if (!string.IsNullOrWhiteSpace(updatedNotes))
                    {
                        await _repository.UpdateAiNotesJsonAsync(
                            sourceCode,
                            parsedId,
                            provider,
                            model,
                            updatedNotes,
                            context.CancellationToken);

                        updatedNotesCount++;
                    }

                    // Candidate capture
                    if (writeCandidates && candidateUpserts.Count < maxCandidatesPerRun)
                    {
                        foreach (var pair in suggestionPairs
                                     .OrderByDescending(x => x.Suggestion.Score)
                                     .ThenBy(x => x.Suggestion.MatchedHash, StringComparer.Ordinal))
                        {
                            if (candidateUpserts.Count >= maxCandidatesPerRun)
                                break;

                            if (!TryBuildCandidate(sourceCode, pair.FromText, pair.Suggestion, candidateMinConfidence, out var candidate))
                                continue;

                            candidateUpserts.Add(candidate);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "LuceneMemorySuggestionsPipelineStep failed. ParsedDefinitionId={ParsedId}", parsedId);
                }
            }

            _logger.LogInformation(
                "LuceneMemorySuggestionsPipelineStep completed. SourceCode={SourceCode}, UpdatedNotes={UpdatedNotesCount}, CandidateUpserts={CandidateCount}",
                sourceCode,
                updatedNotesCount,
                candidateUpserts.Count);

            if (!writeCandidates || candidateUpserts.Count == 0)
                return;

            try
            {
                try
                {
                    var existing = await _candidateRepository.GetExistingRewriteMapKeysAsync(sourceCode, context.CancellationToken);

                    var filtered = candidateUpserts
                        .Where(c =>
                        {
                            var mode = (c.Mode ?? string.Empty).Trim();

                            // Normalize to RewriteMap Mode values
                            var rewriteMode = mode.Equals("MeaningTitle", StringComparison.OrdinalIgnoreCase)
                                ? "Title"
                                : mode;

                            var from = (c.FromText ?? string.Empty).Trim();

                            if (string.IsNullOrWhiteSpace(rewriteMode) || string.IsNullOrWhiteSpace(from))
                                return false;

                            return !existing.Contains($"{rewriteMode}|{from}");
                        })
                        .ToList();

                    if (filtered.Count == 0)
                    {
                        _logger.LogInformation(
                            "LuceneMemorySuggestionsPipelineStep: candidate capture skipped (all already exist in RewriteMap). SourceCode={SourceCode}",
                            sourceCode);

                        return;
                    }

                    await _candidateRepository.UpsertCandidatesAsync(filtered, context.CancellationToken);

                    _logger.LogInformation(
                        "LuceneMemorySuggestionsPipelineStep candidate capture saved. SourceCode={SourceCode}, Count={Count}",
                        sourceCode,
                        filtered.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LuceneMemorySuggestionsPipelineStep: failed to upsert RewriteMapCandidate rows.");
                }

                _logger.LogInformation(
                    "LuceneMemorySuggestionsPipelineStep candidate capture saved. SourceCode={SourceCode}, Count={Count}",
                    sourceCode,
                    candidateUpserts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LuceneMemorySuggestionsPipelineStep: failed to upsert RewriteMapCandidate rows.");
            }
        }

        // NEW METHOD (added)
        private static bool TryBuildCandidate(
            string sourceCode,
            string fromText,
            LuceneSuggestionResult suggestion,
            decimal minConfidence,
            out RewriteMapCandidateUpsert candidate)
        {

            candidate = new RewriteMapCandidateUpsert();

            if (suggestion is null)
                return false;

            fromText = (fromText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fromText))
                return false;

            var toText = (suggestion.SuggestionText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(toText))
                return false;

            if (!IsCandidateSafeForRewriteMap(suggestion.Mode, fromText, toText))
                return false;

            if (string.Equals(fromText, toText, StringComparison.Ordinal))
                return false;

            if (ContainsPlaceholder(fromText) || ContainsPlaceholder(toText))
                return false;

            // Convert lucene score to deterministic confidence bucket (stable behavior)
            var confidence = suggestion.Score >= 2.0 ? 0.90m
                : suggestion.Score >= 1.6 ? 0.80m
                : suggestion.Score >= 1.2 ? 0.70m
                : 0.60m;

            if (confidence < minConfidence)
                return false;

            candidate = new RewriteMapCandidateUpsert
            {
                SourceCode = sourceCode,
                Mode = suggestion.Mode.ToString(),
                FromText = fromText,
                ToText = toText,
                Confidence = confidence
            };

            return true;
        }

        // NEW METHOD (added)
        private static bool IsCandidateSafeForRewriteMap(LuceneSuggestionMode mode, string fromText, string toText)
        {
            if (string.IsNullOrWhiteSpace(fromText) || string.IsNullOrWhiteSpace(toText))
                return false;

            fromText = fromText.Trim();
            toText = toText.Trim();

            if (fromText.Length <= 3 || toText.Length <= 3)
                return false;

            if (fromText.Contains('\n') || fromText.Contains('\r') || fromText.Contains('\t'))
                return false;

            var maxLen = mode == LuceneSuggestionMode.MeaningTitle ? 80
                : mode == LuceneSuggestionMode.Example ? 200
                : 300;

            if (fromText.Length > maxLen || toText.Length > maxLen)
                return false;

            if (LooksNumericHeavy(fromText))
                return false;

            if (LooksSymbolHeavy(fromText))
                return false;

            if (fromText.EndsWith(":", StringComparison.Ordinal))
                return false;

            return true;
        }

        // NEW METHOD (added)
        private static bool LooksNumericHeavy(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var digits = 0;
            var total = 0;

            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                    continue;

                total++;

                if (char.IsDigit(ch))
                    digits++;
            }

            if (total <= 0)
                return false;

            var ratio = (double)digits / total;
            return ratio >= 0.20; // 20% digits = likely unsafe
        }

        // NEW METHOD (added)
        private static bool LooksSymbolHeavy(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var symbols = 0;
            var total = 0;

            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                    continue;

                total++;

                if (!char.IsLetterOrDigit(ch))
                    symbols++;
            }

            if (total <= 0)
                return false;

            var ratio = (double)symbols / total;
            return ratio >= 0.35; // too many symbols = noisy candidate
        }

        // NEW METHOD (added)
        private static bool ContainsPlaceholder(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("[NON_ENGLISH]", StringComparison.Ordinal) ||
                   text.Contains("[BILINGUAL_EXAMPLE]", StringComparison.Ordinal);
        }

        // NEW METHOD (added)
        private static string NormalizeSource(string? sourceCode)
            => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

        // NEW METHOD (added)
        private static bool DirectoryIsEmpty(string path)
        {
            try
            {
                return !Directory.EnumerateFileSystemEntries(path).Any();
            }
            catch
            {
                return true;
            }
        }
    }
}
