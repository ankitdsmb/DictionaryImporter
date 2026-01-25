using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Gateway.Rewriter
{
    using DictionaryImporter.Infrastructure.Persistence;

    public sealed class SqlLuceneSuggestionIndexRepository(
        string connectionString,
        ILogger<SqlLuceneSuggestionIndexRepository> logger)
        : SqlRepo(connectionString, logger), ILuceneSuggestionIndexRepository
    {
        public async Task<IReadOnlyList<LuceneSuggestionIndexRow>> GetRewritePairsAsync(
            string? sourceCode,
            int take,
            int skip,
            CancellationToken cancellationToken)
        {
            take = Clamp(take, 1, 5000);
            skip = Math.Max(0, skip);

            const string sql = @"
;WITH X AS
(
    SELECT
        a.SourceCode,
        a.ParsedDefinitionId,
        a.AiEnhancedDefinition,
        a.AiNotesJson
    FROM dbo.DictionaryEntryAiAnnotation a WITH (NOLOCK)
    WHERE (@SourceCode IS NULL OR a.SourceCode = @SourceCode)
      AND a.AiEnhancedDefinition IS NOT NULL
      AND LTRIM(RTRIM(a.AiEnhancedDefinition)) <> ''
)
SELECT
    x.SourceCode,
    x.ParsedDefinitionId,
    x.AiEnhancedDefinition,
    x.AiNotesJson,
    p.Definition,
    p.DefinitionHash,
    p.MeaningTitle,
    p.MeaningTitleHash
FROM X x
JOIN dbo.DictionaryEntryParsed p WITH (NOLOCK)
    ON p.DictionaryEntryParsedId = x.ParsedDefinitionId
WHERE p.SourceCode = x.SourceCode
ORDER BY x.ParsedDefinitionId ASC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
";

            return await WithConn(async conn =>
            {
                var cmd = new CommandDefinition(
                    sql,
                    new
                    {
                        SourceCode = string.IsNullOrWhiteSpace(sourceCode) ? null : sourceCode.Trim(),
                        Take = take,
                        Skip = skip
                    },
                    cancellationToken: cancellationToken,
                    commandTimeout: 60);

                var rows = (await conn.QueryAsync<RowDto>(cmd)).AsList();
                if (rows.Count == 0)
                    return Array.Empty<LuceneSuggestionIndexRow>();

                return TransformRowsToLucenePairs(rows, cancellationToken);
            }, cancellationToken, fallback: Array.Empty<LuceneSuggestionIndexRow>());
        }

        public async Task<IReadOnlyList<LuceneSuggestionIndexRow>> GetRewritePairsAfterIdAsync(
            string sourceCode,
            long lastParsedDefinitionId,
            int take,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return Array.Empty<LuceneSuggestionIndexRow>();

            take = Clamp(take, 1, 5000);

            const string sql = @"
;WITH X AS
(
    SELECT
        a.SourceCode,
        a.ParsedDefinitionId,
        a.AiEnhancedDefinition,
        a.AiNotesJson
    FROM dbo.DictionaryEntryAiAnnotation a WITH (NOLOCK)
    WHERE a.SourceCode = @SourceCode
      AND a.ParsedDefinitionId > @LastId
      AND a.AiEnhancedDefinition IS NOT NULL
      AND LTRIM(RTRIM(a.AiEnhancedDefinition)) <> ''
)
SELECT TOP (@Take)
    x.SourceCode,
    x.ParsedDefinitionId,
    x.AiEnhancedDefinition,
    x.AiNotesJson,
    p.Definition,
    p.DefinitionHash,
    p.MeaningTitle,
    p.MeaningTitleHash
FROM X x
JOIN dbo.DictionaryEntryParsed p WITH (NOLOCK)
    ON p.DictionaryEntryParsedId = x.ParsedDefinitionId
WHERE p.SourceCode = x.SourceCode
ORDER BY x.ParsedDefinitionId ASC;
";

            return await WithConn(async conn =>
            {
                var cmd = new CommandDefinition(
                    sql,
                    new
                    {
                        SourceCode = sourceCode.Trim(),
                        LastId = lastParsedDefinitionId,
                        Take = take
                    },
                    cancellationToken: cancellationToken,
                    commandTimeout: 60);

                var rows = (await conn.QueryAsync<RowDto>(cmd)).AsList();
                if (rows.Count == 0)
                    return Array.Empty<LuceneSuggestionIndexRow>();

                return TransformRowsToLucenePairs(rows, cancellationToken);
            }, cancellationToken, fallback: Array.Empty<LuceneSuggestionIndexRow>());
        }

        // NEW METHOD (added)
        private static IReadOnlyList<LuceneSuggestionIndexRow> TransformRowsToLucenePairs(
            List<RowDto> rows,
            CancellationToken cancellationToken)
        {
            var ordered = rows
                .Where(r => r is not null)
                .OrderBy(r => r.ParsedDefinitionId)
                .ToList();

            var result = new List<LuceneSuggestionIndexRow>(ordered.Count * 3);

            foreach (var r in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(r.SourceCode))
                    continue;

                var src = r.SourceCode.Trim();
                if (src.Length == 0)
                    continue;

                AddDefinitionPair(result, src, r);
                AddMeaningTitlePair(result, src, r);
                AddExamplePairs(result, src, r);
            }

            return result
                .OrderBy(x => x.SourceCode, StringComparer.Ordinal)
                .ThenBy(x => (int)x.Mode)
                .ThenBy(x => x.OriginalText, StringComparer.Ordinal)
                .ThenBy(x => x.EnhancedText, StringComparer.Ordinal)
                .ToList();
        }

        // NEW METHOD (added)
        private static void AddDefinitionPair(List<LuceneSuggestionIndexRow> result, string src, RowDto r)
        {
            var original = (r.Definition ?? string.Empty).Trim();
            var enhanced = (r.AiEnhancedDefinition ?? string.Empty).Trim();
            var hash = (r.DefinitionHash ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(original)) return;
            if (string.IsNullOrWhiteSpace(enhanced)) return;
            if (string.IsNullOrWhiteSpace(hash)) return;
            if (string.Equals(original, enhanced, StringComparison.Ordinal)) return;

            result.Add(new LuceneSuggestionIndexRow
            {
                SourceCode = src,
                Mode = LuceneSuggestionMode.Definition,
                OriginalText = original,
                EnhancedText = enhanced,
                OriginalTextHash = hash
            });
        }

        // NEW METHOD (added)
        private static void AddMeaningTitlePair(List<LuceneSuggestionIndexRow> result, string src, RowDto r)
        {
            var parsedTitleOriginal = (r.MeaningTitle ?? string.Empty).Trim();
            var parsedTitleHash = (r.MeaningTitleHash ?? string.Empty).Trim();

            var (notesTitleOriginal, notesTitleRewritten) = AiNotesJsonReader.TryReadTitle(r.AiNotesJson);

            var original = !string.IsNullOrWhiteSpace(notesTitleOriginal)
                ? notesTitleOriginal.Trim()
                : parsedTitleOriginal;

            var rewritten = (notesTitleRewritten ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(original)) return;
            if (string.IsNullOrWhiteSpace(rewritten)) return;
            if (string.Equals(original, rewritten, StringComparison.Ordinal)) return;

            var hash = !string.IsNullOrWhiteSpace(parsedTitleHash)
                ? parsedTitleHash
                : DeterministicHashHelper.Sha256Hex(original);

            if (string.IsNullOrWhiteSpace(hash)) return;

            result.Add(new LuceneSuggestionIndexRow
            {
                SourceCode = src,
                Mode = LuceneSuggestionMode.MeaningTitle,
                OriginalText = original,
                EnhancedText = rewritten,
                OriginalTextHash = hash
            });
        }

        // NEW METHOD (added)
        private static void AddExamplePairs(List<LuceneSuggestionIndexRow> result, string src, RowDto r)
        {
            var examples = AiNotesJsonReader.TryReadExamples(r.AiNotesJson, maxExamples: 20);
            if (examples.Count == 0)
                return;

            foreach (var pair in examples)
            {
                var original = (pair.Original ?? string.Empty).Trim();
                var rewritten = (pair.Rewritten ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(original)) continue;
                if (string.IsNullOrWhiteSpace(rewritten)) continue;
                if (string.Equals(original, rewritten, StringComparison.Ordinal)) continue;

                var hash = DeterministicHashHelper.Sha256Hex(original);
                if (string.IsNullOrWhiteSpace(hash))
                    continue;

                result.Add(new LuceneSuggestionIndexRow
                {
                    SourceCode = src,
                    Mode = LuceneSuggestionMode.Example,
                    OriginalText = original,
                    EnhancedText = rewritten,
                    OriginalTextHash = hash
                });
            }
        }

        private sealed class RowDto
        {
            public string? SourceCode { get; set; }
            public long ParsedDefinitionId { get; set; }
            public string? AiEnhancedDefinition { get; set; }
            public string? AiNotesJson { get; set; }
            public string? Definition { get; set; }
            public string? DefinitionHash { get; set; }
            public string? MeaningTitle { get; set; }
            public string? MeaningTitleHash { get; set; }
        }
    }
}
