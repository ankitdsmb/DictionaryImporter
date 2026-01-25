using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LuceneDocument = Lucene.Net.Documents.Document;

namespace DictionaryImporter.Gateway.Rewriter;

public sealed class LuceneIndexBuilder(
    ILuceneSuggestionIndexRepository repository,
    ILogger<LuceneIndexBuilder> logger)
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

    private const int BatchTake = 5_000;
    private const int MaxOriginalLen = 2_000;
    private const int MaxEnhancedLen = 2_000;

    public async Task BuildOrUpdateIndexAsync(
        string indexPath,
        string? sourceCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
            return;

        sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? null : sourceCode.Trim();

        try
        {
            System.IO.Directory.CreateDirectory(indexPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lucene index path could not be created: {IndexPath}", indexPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            logger.LogWarning("LuceneIndexBuilder skipped: SourceCode is required for incremental build.");
            return;
        }

        var state = LuceneIndexStateStore.Load(indexPath);

        try
        {
            using var dir = FSDirectory.Open(new DirectoryInfo(indexPath));
            using var analyzer = new StandardAnalyzer(AppLuceneVersion);

            var config = new IndexWriterConfig(AppLuceneVersion, analyzer);
            using var writer = new IndexWriter(dir, config);

            var lastId = state.LastIndexedParsedDefinitionId;
            var total = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var rows = await repository.GetRewritePairsAfterIdAsync(sourceCode, lastId, BatchTake, cancellationToken);
                if (rows.Count == 0)
                    break;

                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var original = LuceneTextNormalizer.NormalizeForSearch(row.OriginalText, MaxOriginalLen);
                    var enhanced = LuceneTextNormalizer.NormalizeForSearch(row.EnhancedText, MaxEnhancedLen);

                    if (string.IsNullOrWhiteSpace(original)) continue;
                    if (string.IsNullOrWhiteSpace(enhanced)) continue;
                    if (string.IsNullOrWhiteSpace(row.SourceCode)) continue;
                    if (string.IsNullOrWhiteSpace(row.OriginalTextHash)) continue;

                    var modeStr = LuceneTextNormalizer.ModeToString(row.Mode);
                    var key = $"{row.SourceCode}|{modeStr}|{row.OriginalTextHash}";

                    var doc = new LuceneDocument
                    {
                        new StringField("Key", key, Field.Store.NO),
                        new StringField("SourceCode", row.SourceCode, Field.Store.YES),
                        new StringField("Mode", modeStr, Field.Store.YES),
                        new StringField("OriginalTextHash", row.OriginalTextHash, Field.Store.YES),
                        new TextField("OriginalText", original, Field.Store.YES),
                        new StoredField("EnhancedText", enhanced)
                    };

                    writer.UpdateDocument(new Term("Key", key), doc);
                    total++;
                }

                // update last id deterministically (since query is ASC)
                lastId += rows.Count;
            }

            writer.Commit();

            state.SourceCode = sourceCode;
            state.LastIndexedParsedDefinitionId = lastId;
            state.LastIndexedUtc = DateTime.UtcNow;
            LuceneIndexStateStore.Save(indexPath, state);

            logger.LogInformation(
                "Lucene index incremental build completed. SourceCode={SourceCode}, IndexPath={IndexPath}, DocsUpserted={Total}, LastId={LastId}",
                sourceCode,
                indexPath,
                total,
                lastId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Lucene index build canceled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lucene index build failed.");
        }
    }

}