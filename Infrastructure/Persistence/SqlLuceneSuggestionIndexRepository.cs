using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlLuceneSuggestionIndexRepository(
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlLuceneSuggestionIndexRepository> logger)
    : ILuceneSuggestionIndexRepository
{
    private readonly ISqlStoredProcedureExecutor _sp = sp;
    private readonly ILogger<SqlLuceneSuggestionIndexRepository> _logger = logger;

    public async Task<IReadOnlyList<LuceneSuggestionIndexRow>> GetRewritePairsAsync(
        string? sourceCode,
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        take = Helper.SqlRepository.Clamp(take, 1, 5000);
        skip = Math.Max(0, skip);

        try
        {
            var rows = await _sp.QueryAsync<RowDto>(
                "sp_LuceneSuggestionIndex_GetRewritePairs",
                new
                {
                    SourceCode = string.IsNullOrWhiteSpace(sourceCode) ? null : sourceCode.Trim(),
                    Take = take,
                    Skip = skip
                },
                cancellationToken,
                timeoutSeconds: 60);

            if (rows.Count == 0)
                return Array.Empty<LuceneSuggestionIndexRow>();

            return TransformRowsToLucenePairs(rows.ToList(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<LuceneSuggestionIndexRow>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetRewritePairsAsync failed. SourceCode={SourceCode}, Take={Take}, Skip={Skip}",
                sourceCode, take, skip);

            return Array.Empty<LuceneSuggestionIndexRow>();
        }
    }

    public async Task<IReadOnlyList<LuceneSuggestionIndexRow>> GetRewritePairsAfterIdAsync(
        string sourceCode,
        long lastParsedDefinitionId,
        int take,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return Array.Empty<LuceneSuggestionIndexRow>();

        take = Helper.SqlRepository.Clamp(take, 1, 5000);

        try
        {
            var rows = await _sp.QueryAsync<RowDto>(
                "sp_LuceneSuggestionIndex_GetRewritePairsAfterId",
                new
                {
                    SourceCode = sourceCode.Trim(),
                    LastId = lastParsedDefinitionId,
                    Take = take
                },
                cancellationToken,
                timeoutSeconds: 60);

            if (rows.Count == 0)
                return Array.Empty<LuceneSuggestionIndexRow>();

            return TransformRowsToLucenePairs(rows.ToList(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<LuceneSuggestionIndexRow>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetRewritePairsAfterIdAsync failed. SourceCode={SourceCode}, LastId={LastId}, Take={Take}",
                sourceCode, lastParsedDefinitionId, take);

            return Array.Empty<LuceneSuggestionIndexRow>();
        }
    }

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