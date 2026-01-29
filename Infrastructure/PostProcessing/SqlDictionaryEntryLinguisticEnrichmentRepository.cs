using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.PostProcessing;

public sealed class SqlDictionaryEntryLinguisticEnrichmentRepository(string connectionString)
{
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<long> ExtractSynonymsFromCrossReferencesAsync(
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            return await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "sp_DictionaryEntrySynonym_ExtractFromCrossReferences",
                    new { SourceCode = sourceCode },
                    commandType: System.Data.CommandType.StoredProcedure,
                    cancellationToken: ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<IReadOnlyList<CanonicalWordIpaCandidateRow>> GetIpaCandidatesFromParsedFragmentsAsync(
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var rows = await conn.QueryAsync<CanonicalWordIpaCandidateRow>(
                new CommandDefinition(
                    "sp_CanonicalWordPronunciation_GetCandidatesFromParsedFragments",
                    new { SourceCode = sourceCode },
                    commandType: System.Data.CommandType.StoredProcedure,
                    cancellationToken: ct));

            return rows?.ToList() ?? (IReadOnlyList<CanonicalWordIpaCandidateRow>)Array.Empty<CanonicalWordIpaCandidateRow>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<CanonicalWordIpaCandidateRow>();
        }
    }

    public async Task<int> InsertCanonicalWordPronunciationIfMissingAsync(
        long canonicalWordId,
        string localeCode,
        string ipa,
        CancellationToken ct)
    {
        if (canonicalWordId <= 0)
            return 0;

        localeCode = Helper.NormalizeLocaleCode(localeCode);
        ipa = Helper.NormalizeIpa(ipa);

        if (string.IsNullOrWhiteSpace(localeCode))
            return 0;

        if (string.IsNullOrWhiteSpace(ipa))
            return 0;

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            return await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "sp_CanonicalWordPronunciation_InsertIfMissing",
                    new
                    {
                        CanonicalWordId = canonicalWordId,
                        LocaleCode = localeCode,
                        Ipa = ipa
                    },
                    commandType: System.Data.CommandType.StoredProcedure,
                    cancellationToken: ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }
}