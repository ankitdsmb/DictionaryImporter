using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlParsedDefinitionWriter(
    string connectionString,
    GenericSqlBatcher batcher,
    ILogger<SqlParsedDefinitionWriter> logger)
{
    public async Task<long> WriteAsync(
        long dictionaryEntryId,
        ParsedDefinition parsed,
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        if (dictionaryEntryId <= 0 || parsed == null)
            return 0;

        var definitionToStore = parsed.Definition ?? string.Empty;

        var hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(definitionToStore);
        long? nonEnglishTextId = null;

        if (hasNonEnglishText)
        {
            try
            {
                nonEnglishTextId = await Helper.SqlRepository.StoreNonEnglishTextAsync(
                    sp: new SqlStoredProcedureExecutor(connectionString), // local safe call (no DI here)
                    originalText: definitionToStore,
                    sourceCode: sourceCode,
                    fieldType: "Definition",
                    ct: ct,
                    timeoutSeconds: 60);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // never crash importer
                logger.LogDebug(ex,
                    "Non-English text store failed (ignored). SourceCode={SourceCode}",
                    sourceCode);

                nonEnglishTextId = null;
            }
        }

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var ignoreDictionaryEntryId = ShouldPreventDuplicates(sourceCode);

            var parsedId = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "dbo.sp_DictionaryEntryParsed_InsertOrGetId",
                    new
                    {
                        DictionaryEntryId = dictionaryEntryId,
                        ParentParsedId = parsed.ParentParsedId > 0 ? parsed.ParentParsedId : (long?)null,
                        MeaningTitle = parsed.MeaningTitle ?? string.Empty,
                        Definition = definitionToStore,
                        RawFragment = parsed.RawFragment ?? string.Empty,
                        SenseNumber = parsed.SenseNumber,
                        Domain = parsed.Domain,
                        UsageLabel = parsed.UsageLabel,
                        HasNonEnglishText = hasNonEnglishText,
                        NonEnglishTextId = nonEnglishTextId,
                        SourceCode = sourceCode,
                        IgnoreDictionaryEntryId = ignoreDictionaryEntryId
                    },
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: ct,
                    commandTimeout: 60));

            return parsedId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "WriteAsync failed for parsed definition. DictionaryEntryId={DictionaryEntryId}, SourceCode={SourceCode}",
                dictionaryEntryId,
                sourceCode);

            return 0;
        }
    }

    public async Task WriteBatchAsync(
        IEnumerable<(long DictionaryEntryId, ParsedDefinition Parsed, string SourceCode)> entries,
        CancellationToken ct)
    {
        try
        {
            var table = new DataTable();
            table.Columns.Add("DictionaryEntryId", typeof(long));
            table.Columns.Add("ParentParsedId", typeof(long));
            table.Columns.Add("MeaningTitle", typeof(string));
            table.Columns.Add("Definition", typeof(string));
            table.Columns.Add("RawFragment", typeof(string));
            table.Columns.Add("SenseNumber", typeof(int));
            table.Columns.Add("Domain", typeof(string));
            table.Columns.Add("UsageLabel", typeof(string));
            table.Columns.Add("HasNonEnglishText", typeof(bool));
            table.Columns.Add("NonEnglishTextId", typeof(long));
            table.Columns.Add("SourceCode", typeof(string));

            foreach (var entry in entries ?? Enumerable.Empty<(long DictionaryEntryId, ParsedDefinition Parsed, string SourceCode)>())
            {
                ct.ThrowIfCancellationRequested();

                if (entry.DictionaryEntryId <= 0 || entry.Parsed == null)
                    continue;

                var safeSourceCode = Helper.SqlRepository.NormalizeSourceCode(entry.SourceCode);

                var definitionToStore = entry.Parsed.Definition ?? string.Empty;
                var hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(definitionToStore);

                // ✅ Important: Keep batch safe & fast.
                // Do NOT insert DictionaryNonEnglishText inside the batch loop.
                long? nonEnglishTextId = null;

                table.Rows.Add(
                    entry.DictionaryEntryId,
                    entry.Parsed.ParentParsedId > 0 ? entry.Parsed.ParentParsedId : DBNull.Value,
                    entry.Parsed.MeaningTitle ?? string.Empty,
                    definitionToStore,
                    entry.Parsed.RawFragment ?? string.Empty,
                    entry.Parsed.SenseNumber,
                    entry.Parsed.Domain ?? (object)DBNull.Value,
                    entry.Parsed.UsageLabel ?? (object)DBNull.Value,
                    hasNonEnglishText,
                    nonEnglishTextId ?? (object)DBNull.Value,
                    safeSourceCode);
            }

            if (table.Rows.Count == 0)
                return;

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var p = new DynamicParameters();
            p.Add("@Rows", table.AsTableValuedParameter("dbo.DictionaryEntryParsed_BatchType"));

            await conn.ExecuteAsync(new CommandDefinition(
                "dbo.sp_DictionaryEntryParsed_InsertBatch",
                p,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct,
                commandTimeout: 60));

            logger.LogInformation(
                "Inserted batch of {Count} parsed definitions via SP",
                table.Rows.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WriteBatchAsync failed (SP batch insert).");
        }
    }

    private static bool ShouldPreventDuplicates(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return false;

        return sourceCode.Equals("KAIKKI", StringComparison.OrdinalIgnoreCase);
    }
}