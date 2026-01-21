using DictionaryImporter.Infrastructure.Persistence.Batched;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntrySynonymWriter : IDictionaryEntrySynonymWriter, IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlDictionaryEntrySynonymWriter> _logger;
        private GenericSqlBatcher _batcher;
        private bool _ownsBatcher = false;

        // Constructor 1: With batcher (for DI)
        public SqlDictionaryEntrySynonymWriter(
            string connectionString,
            ILogger<SqlDictionaryEntrySynonymWriter> logger,
            GenericSqlBatcher batcher)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _batcher = batcher ?? throw new ArgumentNullException(nameof(batcher));
            _ownsBatcher = false;

            _logger.LogInformation("SqlDictionaryEntrySynonymWriter initialized with injected batcher");
        }

        // Constructor 2: Without batcher (creates its own) - FIXED
        public SqlDictionaryEntrySynonymWriter(
            string connectionString,
            ILogger<SqlDictionaryEntrySynonymWriter> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // FIXED: Create batcher WITHOUT AddConsole (use existing logger)
            _batcher = CreateInternalBatcher();
            _ownsBatcher = true;

            _logger.LogInformation("SqlDictionaryEntrySynonymWriter created internal batcher");
        }

        private GenericSqlBatcher CreateInternalBatcher()
        {
            try
            {
                // Option 1: Use a NullLogger if you don't need batcher logs
                var nullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<GenericSqlBatcher>.Instance;
                return new GenericSqlBatcher(_connectionString, nullLogger);

                // Option 2: Forward logs through our existing logger (simplified)
                // return new GenericSqlBatcher(_connectionString, new ForwardingLogger(_logger));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create internal batcher");
                throw;
            }
        }

        // Forwarding logger implementation
        private class ForwardingLogger : ILogger<GenericSqlBatcher>
        {
            private readonly ILogger _targetLogger;

            public ForwardingLogger(ILogger targetLogger)
            {
                _targetLogger = targetLogger;
            }

            public IDisposable BeginScope<TState>(TState state) => _targetLogger.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => _targetLogger.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _targetLogger.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        public async Task WriteAsync(DictionaryEntrySynonym synonym, CancellationToken ct)
        {
            const string sql = @"
                IF NOT EXISTS (
                    SELECT 1 FROM dbo.DictionaryEntrySynonym
                    WHERE DictionaryEntryParsedId = @DictionaryEntryParsedId
                    AND SynonymText = @SynonymText
                )
                BEGIN
                    INSERT INTO dbo.DictionaryEntrySynonym
                    (DictionaryEntryParsedId, SynonymText, Source, CreatedUtc)
                    VALUES (@DictionaryEntryParsedId, @SynonymText,
                            ISNULL(@Source, 'dictionary'), SYSUTCDATETIME());
                END";

            if (_batcher == null)
            {
                _logger.LogError("Batcher is null, using direct execution");
                await using var conn = new SqlConnection(_connectionString);
                await conn.ExecuteAsync(new CommandDefinition(sql, synonym, cancellationToken: ct));
                return;
            }

            await _batcher.QueueOperationAsync(
                "INSERT_Synonym",
                sql,
                synonym,
                CommandType.Text,
                30);
        }

        public async Task BulkWriteAsync(
            IEnumerable<DictionaryEntrySynonym> synonyms,
            CancellationToken ct)
        {
            const string sql = @"
                INSERT INTO dbo.DictionaryEntrySynonym
                (DictionaryEntryParsedId, SynonymText, Source, CreatedUtc)
                SELECT
                    s.DictionaryEntryParsedId,
                    s.SynonymText,
                    ISNULL(s.Source, 'dictionary'),
                    SYSUTCDATETIME()
                FROM @Synonyms s
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.DictionaryEntrySynonym es
                    WHERE es.DictionaryEntryParsedId = s.DictionaryEntryParsedId
                      AND es.SynonymText = s.SynonymText
                )";

            var synonymTable = CreateSynonymTable(synonyms);
            var param = new { Synonyms = synonymTable.AsTableValuedParameter("dbo.DictionaryEntrySynonymType") };

            if (_batcher == null)
            {
                _logger.LogError("Batcher is null, using direct execution for bulk write");
                await using var conn = new SqlConnection(_connectionString);
                await conn.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct));
                return;
            }

            await _batcher.ExecuteImmediateAsync(sql, param, CommandType.Text, 30, ct);
        }

        public async Task WriteSynonymsForParsedDefinition(
            long parsedDefinitionId,
            IEnumerable<string> synonyms,
            string sourceCode,
            CancellationToken ct)
        {
            var synonymList = synonyms.ToList();
            if (synonymList.Count == 0) return;

            var uniqueSynonyms = synonymList
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogDebug("Writing {Count} unique synonyms for parsed definition {ParsedId}",
                uniqueSynonyms.Count, parsedDefinitionId);

            if (uniqueSynonyms.Count > 10)
            {
                // Use bulk write for larger sets
                var synonymEntities = uniqueSynonyms.Select(s => new DictionaryEntrySynonym
                {
                    DictionaryEntryParsedId = parsedDefinitionId,
                    SynonymText = s,
                    Source = sourceCode,
                    CreatedUtc = DateTime.UtcNow
                });

                await BulkWriteAsync(synonymEntities, ct);
            }
            else
            {
                // Use individual writes for small sets
                foreach (var synonym in uniqueSynonyms)
                {
                    await WriteAsync(new DictionaryEntrySynonym
                    {
                        DictionaryEntryParsedId = parsedDefinitionId,
                        SynonymText = synonym,
                        Source = sourceCode,
                        CreatedUtc = DateTime.UtcNow
                    }, ct);
                }
            }

            // Force flush if using batcher
            if (_batcher != null)
            {
                await _batcher.FlushAllAsync(ct);
            }
        }

        private DataTable CreateSynonymTable(IEnumerable<DictionaryEntrySynonym> synonyms)
        {
            var table = new DataTable();
            table.Columns.Add("DictionaryEntryParsedId", typeof(long));
            table.Columns.Add("SynonymText", typeof(string));
            table.Columns.Add("Source", typeof(string));

            var uniqueSynonyms = synonyms
                .GroupBy(s => new { s.DictionaryEntryParsedId, s.SynonymText })
                .Select(g => g.First())
                .ToList();

            foreach (var synonym in uniqueSynonyms)
            {
                table.Rows.Add(
                    synonym.DictionaryEntryParsedId,
                    synonym.SynonymText,
                    synonym.Source ?? "dictionary");
            }

            return table;
        }

        public void Dispose()
        {
            if (_ownsBatcher)
            {
                _batcher?.Dispose();
            }
        }
    }
}