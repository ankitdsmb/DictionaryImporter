using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Abstractions.Persistence;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlAiAnnotationRepository(
        ISqlStoredProcedureExecutor sp,
        ILogger<SqlAiAnnotationRepository> logger)
        : IAiAnnotationRepository
    {
        private const int DefaultTake = 500;
        private const int MaxExamplesPerParsedIdHardLimit = 50;
        private const int MaxEnhancementBatchSize = 500;

        private readonly ISqlStoredProcedureExecutor _sp = sp;
        private readonly ILogger<SqlAiAnnotationRepository> _logger = logger;

        public async Task<IReadOnlyList<AiDefinitionCandidate>> GetDefinitionCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct)
        {
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);
            take = SqlRepositoryHelper.Clamp(take <= 0 ? DefaultTake : take, 1, 5000);

            try
            {
                return await _sp.QueryAsync<AiDefinitionCandidate>(
                    "sp_AiAnnotation_GetDefinitionCandidates",
                    new { SourceCode = sourceCode, Take = take },
                    ct,
                    timeoutSeconds: 60);
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
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);

            if (parsedDefinitionIds is null || parsedDefinitionIds.Count == 0)
                return new Dictionary<long, IReadOnlyList<string>>();

            maxExamplesPerParsedId = maxExamplesPerParsedId <= 0 ? 10 : maxExamplesPerParsedId;
            maxExamplesPerParsedId = SqlRepositoryHelper.Clamp(maxExamplesPerParsedId, 1, MaxExamplesPerParsedIdHardLimit);

            var ids = SqlRepositoryHelper.NormalizeDistinctIds(parsedDefinitionIds);
            if (ids.Length == 0)
                return new Dictionary<long, IReadOnlyList<string>>();

            try
            {
                var tvp = SqlRepositoryHelper.ToBigIntIdListTvp(ids);

                var rows = await _sp.QueryAsync<ExampleRow>(
                    "sp_AiAnnotation_GetExamplesByParsedIds",
                    new
                    {
                        SourceCode = sourceCode,
                        Ids = tvp,
                        MaxPerId = maxExamplesPerParsedId
                    },
                    ct,
                    timeoutSeconds: 60);

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
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);

            if (parsedDefinitionIds is null || parsedDefinitionIds.Count == 0)
                return new HashSet<long>();

            var ids = SqlRepositoryHelper.NormalizeDistinctIds(parsedDefinitionIds);
            if (ids.Length == 0)
                return new HashSet<long>();

            provider = SqlRepositoryHelper.NormalizeString(provider, SqlRepositoryHelper.DefaultProvider);
            model = SqlRepositoryHelper.NormalizeString(model, SqlRepositoryHelper.DefaultModel);

            if (provider.Length > 64) provider = provider.Substring(0, 64);
            if (model.Length > 128) model = model.Substring(0, 128);

            try
            {
                var tvp = SqlRepositoryHelper.ToBigIntIdListTvp(ids);

                var found = await _sp.QueryAsync<long>(
                    "sp_AiAnnotation_GetAlreadyEnhancedParsedIds",
                    new
                    {
                        SourceCode = sourceCode,
                        Provider = provider,
                        Model = model,
                        Ids = tvp
                    },
                    ct,
                    timeoutSeconds: 60);

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
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);

            if (enhancements is null || enhancements.Count == 0)
                return;

            try
            {
                var ordered = enhancements
                    .Where(x => x is not null)
                    .Where(x => x.ParsedDefinitionId > 0)
                    .OrderBy(x => x.ParsedDefinitionId)
                    .Take(MaxEnhancementBatchSize)
                    .ToList();

                if (ordered.Count == 0)
                    return;

                foreach (var e in ordered)
                {
                    ct.ThrowIfCancellationRequested();

                    var provider = SqlRepositoryHelper.NormalizeString(e.Provider, SqlRepositoryHelper.DefaultProvider);
                    var model = SqlRepositoryHelper.NormalizeString(e.Model, SqlRepositoryHelper.DefaultModel);

                    if (provider.Length > 64) provider = provider.Substring(0, 64);
                    if (model.Length > 128) model = model.Substring(0, 128);

                    var original = SqlRepositoryHelper.Truncate(e.OriginalDefinition, 4000);
                    var enhanced = SqlRepositoryHelper.Truncate(e.AiEnhancedDefinition, 4000);
                    var notesJson = string.IsNullOrWhiteSpace(e.AiNotesJson) ? "{}" : e.AiNotesJson.Trim();

                    await _sp.ExecuteAsync(
                        "sp_AiAnnotation_SaveEnhancement",
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
                        ct,
                        timeoutSeconds: 60);
                }
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
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);
            if (parsedDefinitionId <= 0) return null;

            provider = SqlRepositoryHelper.NormalizeString(provider, SqlRepositoryHelper.DefaultProvider);
            model = SqlRepositoryHelper.NormalizeString(model, SqlRepositoryHelper.DefaultModel);

            if (provider.Length > 64) provider = provider.Substring(0, 64);
            if (model.Length > 128) model = model.Substring(0, 128);

            try
            {
                return await _sp.ExecuteScalarAsync<string?>(
                    "sp_AiAnnotation_GetAiNotesJson",
                    new
                    {
                        SourceCode = sourceCode,
                        ParsedDefinitionId = parsedDefinitionId,
                        Provider = provider,
                        Model = model
                    },
                    cancellationToken,
                    timeoutSeconds: 30);
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
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);
            if (parsedDefinitionId <= 0) return;

            provider = SqlRepositoryHelper.NormalizeString(provider, SqlRepositoryHelper.DefaultProvider);
            model = SqlRepositoryHelper.NormalizeString(model, SqlRepositoryHelper.DefaultModel);

            if (provider.Length > 64) provider = provider.Substring(0, 64);
            if (model.Length > 128) model = model.Substring(0, 128);

            var json = string.IsNullOrWhiteSpace(aiNotesJson) ? "{}" : aiNotesJson.Trim();

            try
            {
                await _sp.ExecuteAsync(
                    "sp_AiAnnotation_UpdateAiNotesJson",
                    new
                    {
                        SourceCode = sourceCode,
                        ParsedDefinitionId = parsedDefinitionId,
                        Provider = provider,
                        Model = model,
                        AiNotesJson = json
                    },
                    cancellationToken,
                    timeoutSeconds: 30);
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
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);
            if (parsedDefinitionId <= 0) return null;

            try
            {
                return await _sp.ExecuteScalarAsync<string?>(
                    "sp_AiAnnotation_GetOriginalDefinition",
                    new
                    {
                        SourceCode = sourceCode,
                        ParsedDefinitionId = parsedDefinitionId
                    },
                    cancellationToken,
                    timeoutSeconds: 30);
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
    }
}
