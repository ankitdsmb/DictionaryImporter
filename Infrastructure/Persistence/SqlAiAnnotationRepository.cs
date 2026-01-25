using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Core.Abstractions.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlAiAnnotationRepository(
        string connectionString,
        ILogger<SqlAiAnnotationRepository> logger)
        : IAiAnnotationRepository
    {
        private const int DefaultTake = 500;
        private const int MaxExamplesPerParsedIdHardLimit = 50;
        private const int MaxEnhancementBatchSize = 500;

        private const string DefaultProvider = "RuleBased";
        private const string DefaultModel = "DictionaryRewriteV1";

        private readonly string _connectionString = connectionString;
        private readonly ILogger<SqlAiAnnotationRepository> _logger = logger;

        public async Task<IReadOnlyList<AiDefinitionCandidate>> GetDefinitionCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct)
        {
            sourceCode = Normalize(sourceCode, "UNKNOWN");
            take = Clamp(take <= 0 ? DefaultTake : take, 1, 5000);

            const string sql = """
SELECT TOP (@Take)
    dep.DictionaryEntryParsedId AS ParsedDefinitionId,
    ISNULL(dep.Definition, '') AS DefinitionText,
    ISNULL(dep.MeaningTitle, '') AS MeaningTitle,
    '' AS ExampleText
FROM dbo.DictionaryEntryParsed dep WITH (NOLOCK)
WHERE
    dep.SourceCode = @SourceCode
    AND dep.Definition IS NOT NULL
    AND LTRIM(RTRIM(dep.Definition)) <> ''
ORDER BY
    dep.DictionaryEntryParsedId ASC;
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows = await conn.QueryAsync<AiDefinitionCandidate>(
                    new CommandDefinition(
                        sql,
                        new { SourceCode = sourceCode, Take = take },
                        cancellationToken: ct,
                        commandTimeout: 60));

                return rows.AsList();
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<AiDefinitionCandidate>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDefinitionCandidatesAsync failed. SourceCode={SourceCode}", sourceCode);
                return Array.Empty<AiDefinitionCandidate>();
            }
        }

        public async Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> GetExamplesByParsedIdsAsync(
            string sourceCode,
            IReadOnlyList<long> parsedDefinitionIds,
            int maxExamplesPerParsedId,
            CancellationToken ct)
        {
            sourceCode = Normalize(sourceCode, "UNKNOWN");

            if (parsedDefinitionIds is null || parsedDefinitionIds.Count == 0)
                return new Dictionary<long, IReadOnlyList<string>>();

            maxExamplesPerParsedId = maxExamplesPerParsedId <= 0 ? 10 : maxExamplesPerParsedId;
            maxExamplesPerParsedId = Clamp(maxExamplesPerParsedId, 1, MaxExamplesPerParsedIdHardLimit);

            var ids = parsedDefinitionIds
                .Where(x => x > 0)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            if (ids.Length == 0)
                return new Dictionary<long, IReadOnlyList<string>>();

            const string sql = """
WITH ex AS
(
    SELECT
        e.DictionaryEntryParsedId AS ParsedDefinitionId,
        e.DictionaryEntryExampleId AS ExampleId,
        e.ExampleText AS ExampleText,
        ROW_NUMBER() OVER (
            PARTITION BY e.DictionaryEntryParsedId
            ORDER BY e.DictionaryEntryExampleId ASC
        ) AS rn
    FROM dbo.DictionaryEntryExample e WITH (NOLOCK)
    WHERE
        e.SourceCode = @SourceCode
        AND e.DictionaryEntryParsedId IN @Ids
        AND e.ExampleText IS NOT NULL
        AND LTRIM(RTRIM(e.ExampleText)) <> ''

        AND ISNULL(e.HasNonEnglishText, 0) = 0
        AND e.NonEnglishTextId IS NULL
        AND e.ExampleText <> '[NON_ENGLISH]'
        AND e.ExampleText <> '[BILINGUAL_EXAMPLE]'
)
SELECT
    ParsedDefinitionId,
    ExampleId,
    ExampleText
FROM ex
WHERE rn <= @MaxPerId
ORDER BY ParsedDefinitionId ASC, ExampleId ASC;
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows = (await conn.QueryAsync<ExampleRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            SourceCode = sourceCode,
                            Ids = ids,
                            MaxPerId = maxExamplesPerParsedId
                        },
                        cancellationToken: ct,
                        commandTimeout: 60))).AsList();

                if (rows.Count == 0)
                    return new Dictionary<long, IReadOnlyList<string>>();

                return rows
                    .GroupBy(x => x.ParsedDefinitionId)
                    .ToDictionary(
                        g => g.Key,
                        g => (IReadOnlyList<string>)g
                            .OrderBy(x => x.ExampleId)
                            .Select(x => (x.ExampleText ?? string.Empty).Trim())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToList());
            }
            catch (OperationCanceledException)
            {
                return new Dictionary<long, IReadOnlyList<string>>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExamplesByParsedIdsAsync failed. SourceCode={SourceCode}", sourceCode);
                return new Dictionary<long, IReadOnlyList<string>>();
            }
        }

        public async Task<IReadOnlySet<long>> GetAlreadyEnhancedParsedIdsAsync(
            string sourceCode,
            IReadOnlyList<long> parsedDefinitionIds,
            string provider,
            string model,
            CancellationToken ct)
        {
            sourceCode = Normalize(sourceCode, "UNKNOWN");

            if (parsedDefinitionIds is null || parsedDefinitionIds.Count == 0)
                return new HashSet<long>();

            var ids = parsedDefinitionIds
                .Where(x => x > 0)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            if (ids.Length == 0)
                return new HashSet<long>();

            provider = Normalize(provider, DefaultProvider);
            model = Normalize(model, DefaultModel);

            if (provider.Length > 64) provider = provider.Substring(0, 64);
            if (model.Length > 128) model = model.Substring(0, 128);

            const string sql = """
SELECT
    a.ParsedDefinitionId
FROM dbo.DictionaryEntryAiAnnotation a WITH (NOLOCK)
WHERE
    a.SourceCode = @SourceCode
    AND a.Provider = @Provider
    AND a.Model = @Model
    AND a.ParsedDefinitionId IN @Ids;
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var found = await conn.QueryAsync<long>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            SourceCode = sourceCode,
                            Provider = provider,
                            Model = model,
                            Ids = ids
                        },
                        cancellationToken: ct,
                        commandTimeout: 60));

                return new HashSet<long>(found);
            }
            catch (OperationCanceledException)
            {
                return new HashSet<long>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAlreadyEnhancedParsedIdsAsync failed. SourceCode={SourceCode}", sourceCode);
                return new HashSet<long>();
            }
        }

        public async Task SaveAiEnhancementsAsync(
            string sourceCode,
            IReadOnlyList<AiDefinitionEnhancement> enhancements,
            CancellationToken ct)
        {
            sourceCode = Normalize(sourceCode, "UNKNOWN");

            if (enhancements is null || enhancements.Count == 0)
                return;

            const string sql = """
MERGE dbo.DictionaryEntryAiAnnotation WITH (HOLDLOCK) AS target
USING (
    SELECT
        @SourceCode AS SourceCode,
        @ParsedDefinitionId AS ParsedDefinitionId,
        @Provider AS Provider,
        @Model AS Model,
        @OriginalDefinition AS OriginalDefinition,
        @AiEnhancedDefinition AS AiEnhancedDefinition,
        @AiNotesJson AS AiNotesJson
) AS source
ON
    target.SourceCode = source.SourceCode
    AND target.ParsedDefinitionId = source.ParsedDefinitionId
    AND target.Provider = source.Provider
    AND target.Model = source.Model
WHEN MATCHED THEN
    UPDATE SET
        target.OriginalDefinition = source.OriginalDefinition,
        target.AiEnhancedDefinition = source.AiEnhancedDefinition,
        target.AiNotesJson = source.AiNotesJson,
        target.UpdatedUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (SourceCode, ParsedDefinitionId, Provider, Model, OriginalDefinition, AiEnhancedDefinition, AiNotesJson, CreatedUtc, UpdatedUtc)
    VALUES (source.SourceCode, source.ParsedDefinitionId, source.Provider, source.Model, source.OriginalDefinition, source.AiEnhancedDefinition, source.AiNotesJson, SYSUTCDATETIME(), SYSUTCDATETIME());
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var tx = conn.BeginTransaction();

                var ordered = enhancements
                    .Where(x => x is not null)
                    .Where(x => x.ParsedDefinitionId > 0)
                    .OrderBy(x => x.ParsedDefinitionId)
                    .Take(MaxEnhancementBatchSize)
                    .ToList();

                foreach (var e in ordered)
                {
                    ct.ThrowIfCancellationRequested();

                    var provider = Normalize(e.Provider, DefaultProvider);
                    var model = Normalize(e.Model, DefaultModel);

                    if (provider.Length > 64) provider = provider.Substring(0, 64);
                    if (model.Length > 128) model = model.Substring(0, 128);

                    var original = Trunc(e.OriginalDefinition, 4000);
                    var enhanced = Trunc(e.AiEnhancedDefinition, 4000);
                    var notesJson = string.IsNullOrWhiteSpace(e.AiNotesJson) ? "{}" : e.AiNotesJson.Trim();

                    await conn.ExecuteAsync(new CommandDefinition(
                        sql,
                        new
                        {
                            SourceCode = sourceCode,
                            ParsedDefinitionId = e.ParsedDefinitionId,
                            Provider = provider,
                            Model = model,
                            OriginalDefinition = original,
                            AiEnhancedDefinition = enhanced,
                            AiNotesJson = notesJson
                        },
                        transaction: tx,
                        cancellationToken: ct,
                        commandTimeout: 60));
                }

                tx.Commit();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("SaveAiEnhancementsAsync cancelled. SourceCode={SourceCode}", sourceCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveAiEnhancementsAsync failed. SourceCode={SourceCode}", sourceCode);
            }
        }

        public async Task<string?> GetAiNotesJsonAsync(
            string sourceCode,
            long parsedDefinitionId,
            string provider,
            string model,
            CancellationToken cancellationToken)
        {
            sourceCode = Normalize(sourceCode, "UNKNOWN");
            if (parsedDefinitionId <= 0) return null;

            provider = Normalize(provider, DefaultProvider);
            model = Normalize(model, DefaultModel);

            if (provider.Length > 64) provider = provider.Substring(0, 64);
            if (model.Length > 128) model = model.Substring(0, 128);

            const string sql = @"
SELECT a.AiNotesJson
FROM dbo.DictionaryEntryAiAnnotation a WITH (NOLOCK)
WHERE a.SourceCode = @SourceCode
  AND a.ParsedDefinitionId = @ParsedDefinitionId
  AND a.Provider = @Provider
  AND a.Model = @Model;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                    sql,
                    new
                    {
                        SourceCode = sourceCode,
                        ParsedDefinitionId = parsedDefinitionId,
                        Provider = provider,
                        Model = model
                    },
                    cancellationToken: cancellationToken,
                    commandTimeout: 30));
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAiNotesJsonAsync failed. SourceCode={SourceCode}, ParsedDefinitionId={ParsedDefinitionId}", sourceCode, parsedDefinitionId);
                return null;
            }
        }

        public async Task UpdateAiNotesJsonAsync(
            string sourceCode,
            long parsedDefinitionId,
            string provider,
            string model,
            string aiNotesJson,
            CancellationToken cancellationToken)
        {
            sourceCode = Normalize(sourceCode, "UNKNOWN");
            if (parsedDefinitionId <= 0) return;

            provider = Normalize(provider, DefaultProvider);
            model = Normalize(model, DefaultModel);

            if (provider.Length > 64) provider = provider.Substring(0, 64);
            if (model.Length > 128) model = model.Substring(0, 128);

            var json = string.IsNullOrWhiteSpace(aiNotesJson) ? "{}" : aiNotesJson.Trim();

            const string sql = @"
UPDATE dbo.DictionaryEntryAiAnnotation
SET AiNotesJson = @AiNotesJson,
    UpdatedUtc = SYSUTCDATETIME()
WHERE SourceCode = @SourceCode
  AND ParsedDefinitionId = @ParsedDefinitionId
  AND Provider = @Provider
  AND Model = @Model;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                await conn.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        SourceCode = sourceCode,
                        ParsedDefinitionId = parsedDefinitionId,
                        Provider = provider,
                        Model = model,
                        AiNotesJson = json
                    },
                    cancellationToken: cancellationToken,
                    commandTimeout: 30));
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateAiNotesJsonAsync failed. SourceCode={SourceCode}, ParsedDefinitionId={ParsedDefinitionId}", sourceCode, parsedDefinitionId);
            }
        }

        public async Task<string?> GetOriginalDefinitionAsync(
            string sourceCode,
            long parsedDefinitionId,
            CancellationToken cancellationToken)
        {
            sourceCode = Normalize(sourceCode, "UNKNOWN");
            if (parsedDefinitionId <= 0) return null;

            const string sql = @"
SELECT p.Definition
FROM dbo.DictionaryEntryParsed p WITH (NOLOCK)
WHERE p.DictionaryEntryParsedId = @ParsedDefinitionId
  AND p.SourceCode = @SourceCode;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                    sql,
                    new
                    {
                        SourceCode = sourceCode,
                        ParsedDefinitionId = parsedDefinitionId
                    },
                    cancellationToken: cancellationToken,
                    commandTimeout: 30));
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetOriginalDefinitionAsync failed. SourceCode={SourceCode}, ParsedDefinitionId={ParsedDefinitionId}", sourceCode, parsedDefinitionId);
                return null;
            }
        }

        private sealed class ExampleRow
        {
            public long ParsedDefinitionId { get; set; }
            public long ExampleId { get; set; }
            public string? ExampleText { get; set; }
        }

        private static string Normalize(string? v, string fallback)
        {
            v = string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
            return v;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static string Trunc(string? v, int maxLen)
        {
            var t = (v ?? string.Empty).Trim();
            if (maxLen <= 0) return t;
            return t.Length > maxLen ? t.Substring(0, maxLen) : t;
        }
    }
}
