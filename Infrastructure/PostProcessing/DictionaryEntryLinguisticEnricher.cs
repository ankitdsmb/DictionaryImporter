using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.PostProcessing;

public sealed class DictionaryEntryLinguisticEnricher(
    string connectionString,
    IPartOfSpeechInfererV2 posInferer,
    IDictionaryEntryPartOfSpeechRepository posRepository,
    ILogger<DictionaryEntryLinguisticEnricher> logger)
{
    private readonly string _connectionString =
        connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly IPartOfSpeechInfererV2 _posInferer =
        posInferer ?? throw new ArgumentNullException(nameof(posInferer));

    private readonly IDictionaryEntryPartOfSpeechRepository _posRepository =
        posRepository ?? throw new ArgumentNullException(nameof(posRepository));

    private readonly ILogger<DictionaryEntryLinguisticEnricher> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ExecuteAsync(
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        _logger.LogInformation(
            "Linguistic enrichment started | SourceCode={SourceCode}",
            sourceCode);

        try
        {
            await InferAndPersistPartOfSpeech(sourceCode, ct);

            await BackfillExplicitPartOfSpeechConfidence(sourceCode, ct);

            await PersistPartOfSpeechHistory(sourceCode, ct);

            await ExtractSynonymsFromCrossReferences(sourceCode, ct);

            await EnrichCanonicalWordIpaFromDefinition(sourceCode, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Linguistic enrichment failed (non-fatal) | SourceCode={SourceCode}",
                sourceCode);
        }

        _logger.LogInformation(
            "Linguistic enrichment completed | SourceCode={SourceCode}",
            sourceCode);
    }

    // ============================================================
    // Part of Speech (Repository-driven)
    // ============================================================

    private async Task InferAndPersistPartOfSpeech(
        string sourceCode,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "POS inference started | SourceCode={SourceCode}",
            sourceCode);

        IReadOnlyList<(long EntryId, string Definition)> rows;

        try
        {
            rows = await _posRepository.GetEntriesNeedingPosAsync(sourceCode, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "POS inference query failed (non-fatal) | SourceCode={SourceCode}",
                sourceCode);

            return;
        }

        var updated = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (row.EntryId <= 0)
                continue;

            if (string.IsNullOrWhiteSpace(row.Definition))
                continue;

            object resultObj;

            try
            {
                resultObj = _posInferer.InferWithConfidence(row.Definition);
            }
            catch
            {
                continue;
            }

            // Avoid strong typing to missing model compile issue.
            // Expected: result.Pos (string) + result.Confidence (decimal/int)
            var pos = TryReadPropertyAsString(resultObj, "Pos");
            if (string.IsNullOrWhiteSpace(pos) || pos == "unk")
                continue;

            pos = Helper.NormalizePartOfSpeech(pos);
            if (string.IsNullOrWhiteSpace(pos) || pos == "unk")
                continue;

            var confidenceValue = TryReadPropertyAsDecimal(resultObj, "Confidence");
            var confidenceInt = NormalizeConfidenceToPercent(confidenceValue);

            int affected;

            try
            {
                affected = await _posRepository.UpdatePartOfSpeechIfUnknownAsync(
                    row.EntryId,
                    pos,
                    confidenceInt,
                    ct);
            }
            catch
            {
                continue;
            }

            if (affected > 0)
                updated++;
        }

        _logger.LogInformation(
            "POS inference completed | SourceCode={SourceCode} | Updated={Count}",
            sourceCode,
            updated);
    }

    private async Task BackfillExplicitPartOfSpeechConfidence(
        string sourceCode,
        CancellationToken ct)
    {
        try
        {
            var rows = await _posRepository.BackfillConfidenceAsync(sourceCode, ct);

            _logger.LogInformation(
                "POS confidence backfilled | SourceCode={SourceCode} | Rows={Rows}",
                sourceCode,
                rows);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "POS confidence backfill failed (non-fatal) | SourceCode={SourceCode}",
                sourceCode);
        }
    }

    private async Task PersistPartOfSpeechHistory(
        string sourceCode,
        CancellationToken ct)
    {
        try
        {
            await _posRepository.PersistHistoryAsync(sourceCode, ct);

            _logger.LogInformation(
                "POS history persisted | SourceCode={SourceCode}",
                sourceCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "POS history persist failed (non-fatal) | SourceCode={SourceCode}",
                sourceCode);
        }
    }

    // ============================================================
    // Synonyms extracted from CrossRefs (SP-based repository)
    // ============================================================

    private async Task ExtractSynonymsFromCrossReferences(
        string sourceCode,
        CancellationToken ct)
    {
        var repo = new SqlDictionaryEntryLinguisticEnrichmentRepository(_connectionString);

        long rows;

        try
        {
            rows = await repo.ExtractSynonymsFromCrossReferencesAsync(sourceCode, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Synonym extraction failed (non-fatal) | SourceCode={SourceCode}",
                sourceCode);

            return;
        }

        _logger.LogInformation(
            "Synonyms extracted from cross-references | SourceCode={SourceCode} | Rows={Rows}",
            sourceCode,
            rows);
    }

    // ============================================================
    // IPA enrichment (SP-based repository)
    // ============================================================

    private async Task EnrichCanonicalWordIpaFromDefinition(
        string sourceCode,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "IPA enrichment started | SourceCode={SourceCode}",
            sourceCode);

        var repo = new SqlDictionaryEntryLinguisticEnrichmentRepository(_connectionString);

        IReadOnlyList<CanonicalWordIpaCandidateRow> rows;

        try
        {
            rows = await repo.GetIpaCandidatesFromParsedFragmentsAsync(sourceCode, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "IPA candidate fetch failed (non-fatal) | SourceCode={SourceCode}",
                sourceCode);

            return;
        }

        var inserted = 0;
        var candidates = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (row is null || row.CanonicalWordId <= 0)
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.RawFragment))
            {
                skipped++;
                continue;
            }

            IReadOnlyDictionary<string, string> ipaMap;

            try
            {
                ipaMap = Helper.GenericIpaExtractor.ExtractIpaWithLocale(row.RawFragment);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (ipaMap.Count == 0)
            {
                skipped++;
                continue;
            }

            candidates += ipaMap.Count;

            foreach (var kv in ipaMap)
            {
                var rawIpa = kv.Key;
                var locale = kv.Value;

                if (string.IsNullOrWhiteSpace(rawIpa) ||
                    string.IsNullOrWhiteSpace(locale))
                {
                    skipped++;
                    continue;
                }

                string normalizedIpa;

                try
                {
                    normalizedIpa = IpaNormalizer.Normalize(rawIpa);
                }
                catch
                {
                    skipped++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(normalizedIpa))
                {
                    skipped++;
                    continue;
                }

                int affected;

                try
                {
                    affected = await repo.InsertCanonicalWordPronunciationIfMissingAsync(
                        row.CanonicalWordId,
                        locale,
                        normalizedIpa,
                        ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    skipped++;
                    continue;
                }

                if (affected > 0)
                    inserted++;
            }
        }

        _logger.LogInformation(
            "IPA enrichment completed | SourceCode={SourceCode} | Candidates={Candidates} | Inserted={Inserted} | Skipped={Skipped}",
            sourceCode,
            candidates,
            inserted,
            skipped);
    }

    // ============================================================
    // NEW METHODS (added)
    // ============================================================

    private static string? TryReadPropertyAsString(object obj, string propertyName)
    {
        if (obj is null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            var value = prop?.GetValue(obj);

            return value is string s && !string.IsNullOrWhiteSpace(s)
                ? s.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static decimal TryReadPropertyAsDecimal(object obj, string propertyName)
    {
        if (obj is null || string.IsNullOrWhiteSpace(propertyName))
            return 0;

        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            var value = prop?.GetValue(obj);

            if (value is null)
                return 0;

            if (value is decimal d)
                return d;

            if (value is double db)
                return (decimal)db;

            if (value is float f)
                return (decimal)f;

            if (value is int i)
                return i;

            if (value is long l)
                return l;

            if (decimal.TryParse(value.ToString(), out var parsed))
                return parsed;

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int NormalizeConfidenceToPercent(decimal confidence)
    {
        // If inferer returns 0..1, scale to 0..100
        if (confidence >= 0m && confidence <= 1m)
            confidence = confidence * 100m;

        if (confidence < 0m) confidence = 0m;
        if (confidence > 100m) confidence = 100m;

        return (int)Math.Round(confidence, MidpointRounding.AwayFromZero);
    }
}