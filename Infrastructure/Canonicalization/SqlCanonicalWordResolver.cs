using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Canonicalization;

public sealed class SqlCanonicalWordResolver(
    string connectionString,
    ILogger<SqlCanonicalWordResolver> logger)
    : ICanonicalWordResolver
{
    private readonly string _connectionString =
        connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly ILogger<SqlCanonicalWordResolver> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ResolveAsync(
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        _logger.LogInformation(
            "CanonicalResolver started | Source={Source}",
            sourceCode);

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var result =
                await conn.QuerySingleOrDefaultAsync<CanonicalResolverResultRow>(
                    new CommandDefinition(
                        "sp_CanonicalWord_ResolveBySource",
                        new { SourceCode = sourceCode },
                        commandType: CommandType.StoredProcedure,
                        cancellationToken: ct,
                        commandTimeout: 0));

            if (result is null)
            {
                _logger.LogWarning(
                    "CanonicalResolver completed | Source={Source} | NoResultReturned",
                    sourceCode);
                return;
            }

            _logger.LogInformation(
                "CanonicalResolver completed | Source={Source} | UpdatedNormalized={UpdatedNormalized} | InsertedCanonical={InsertedCanonical} | LinkedEntries={LinkedEntries} | Unlinked={Unlinked}",
                sourceCode,
                result.UpdatedNormalizedWord,
                result.InsertedCanonicalWords,
                result.LinkedEntries,
                result.UnlinkedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // STRICT: never crash importer pipeline
            _logger.LogError(
                ex,
                "CanonicalResolver FAILED (non-fatal) | Source={Source}",
                sourceCode);
        }
    }

    private sealed class CanonicalResolverResultRow
    {
        public long UpdatedNormalizedWord { get; init; }
        public long InsertedCanonicalWords { get; init; }
        public long LinkedEntries { get; init; }
        public long UnlinkedEntries { get; init; }
    }
}