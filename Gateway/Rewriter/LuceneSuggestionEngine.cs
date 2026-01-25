using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace DictionaryImporter.Gateway.Rewriter
{
    public sealed class LuceneSuggestionEngine(
        string indexPath,
        ILogger<LuceneSuggestionEngine> logger)
        : ILuceneSuggestionEngine
    {
        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private const int MaxQueryLen = 800;
        private const int PreviewLen = 120;

        public Task<IReadOnlyList<LuceneSuggestionResult>> GetSuggestionsAsync(
            string sourceCode,
            LuceneSuggestionMode mode,
            string inputText,
            int maxSuggestions,
            double minScore,
            CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceCode)) return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());
                if (string.IsNullOrWhiteSpace(indexPath)) return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());
                if (string.IsNullOrWhiteSpace(inputText)) return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());

                if (maxSuggestions <= 0) return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());
                if (maxSuggestions > 10) maxSuggestions = 10;

                if (!System.IO.Directory.Exists(indexPath))
                    return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());

                cancellationToken.ThrowIfCancellationRequested();

                using var dir = FSDirectory.Open(new DirectoryInfo(indexPath));
                if (!DirectoryReader.IndexExists(dir))
                    return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());

                using var reader = DirectoryReader.Open(dir);
                var searcher = new IndexSearcher(reader);

                using var analyzer = new StandardAnalyzer(AppLuceneVersion);

                var normalized = LuceneTextNormalizer.NormalizeForSearch(inputText, MaxQueryLen);
                if (string.IsNullOrWhiteSpace(normalized))
                    return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());

                var modeStr = LuceneTextNormalizer.ModeToString(mode);

                // Deterministic query:
                // Must match SourceCode + Mode
                // Should match OriginalText tokens
                var parser = new QueryParser(AppLuceneVersion, "OriginalText", analyzer)
                {
                    DefaultOperator = Operator.AND
                };

                Query textQuery;
                try
                {
                    textQuery = parser.Parse(QueryParserBase.Escape(normalized));
                }
                catch
                {
                    // Parser can fail on edge chars - return empty deterministically
                    return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());
                }

                var boolean = new BooleanQuery
                {
                    { new TermQuery(new Term("SourceCode", sourceCode)), Occur.MUST },
                    { new TermQuery(new Term("Mode", modeStr)), Occur.MUST },
                    { textQuery, Occur.MUST }
                };

                // Fetch extra hits so we can deterministically filter and then take top-N
                var topDocs = searcher.Search(boolean, Math.Max(50, maxSuggestions * 10));
                if (topDocs?.ScoreDocs == null || topDocs.ScoreDocs.Length == 0)
                    return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());

                // Deterministic tie-break ordering:
                // 1) score desc (Lucene)
                // 2) docId asc
                var ordered = topDocs.ScoreDocs
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Doc)
                    .ToList();

                var results = new List<LuceneSuggestionResult>();

                foreach (var hit in ordered)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (hit.Score < minScore)
                        continue;

                    var doc = searcher.Doc(hit.Doc);

                    var enhanced = doc.Get("EnhancedText") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(enhanced))
                        continue;

                    var matchedHash = doc.Get("OriginalTextHash") ?? string.Empty;
                    var matchedOriginal = doc.Get("OriginalText") ?? string.Empty;

                    results.Add(new LuceneSuggestionResult
                    {
                        Mode = mode,
                        SuggestionText = enhanced.Trim(),
                        Score = hit.Score,
                        MatchedHash = matchedHash.Trim(),
                        MatchedOriginalPreview = LuceneTextNormalizer.Preview(matchedOriginal, PreviewLen),
                        Source = "lucene-memory"
                    });

                    if (results.Count >= maxSuggestions)
                        break;
                }

                return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(results);
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lucene suggestion engine failed.");
                return Task.FromResult<IReadOnlyList<LuceneSuggestionResult>>(Array.Empty<LuceneSuggestionResult>());
            }
        }
    }
}
