using Dapper;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.PostProcessing
{
    public sealed class DictionaryParsedDefinitionProcessor
    {
        private readonly string _connectionString;
        private readonly IDictionaryDefinitionParser _definitionParser;
        private readonly SqlParsedDefinitionWriter _parsedWriter;
        private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;

        public DictionaryParsedDefinitionProcessor(
            string connectionString,
            IDictionaryDefinitionParser definitionParser,
            SqlParsedDefinitionWriter parsedWriter,
            ILogger<DictionaryParsedDefinitionProcessor> logger)
        {
            _connectionString = connectionString;
            _definitionParser = definitionParser;
            _parsedWriter = parsedWriter;
            _logger = logger;
        }

        public async Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // --------------------------------------------------
            // 1. Load DictionaryEntry domain objects
            // --------------------------------------------------
            var entries =
                await conn.QueryAsync<DictionaryEntry>(
                    """
                    SELECT *
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode
                      AND Definition IS NOT NULL
                    """,
                    new { SourceCode = sourceCode });

            int senseCount = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                // --------------------------------------------------
                // 2. Parse into flat ParsedDefinition records
                // --------------------------------------------------
                var parsedDefinitions =
                    _definitionParser.Parse(entry);

                if (parsedDefinitions == null)
                    continue;

                // --------------------------------------------------
                // 3. Persist parsed definitions
                //    (hierarchy resolved by writer/database)
                // --------------------------------------------------
                foreach (var parsed in parsedDefinitions)
                {
                    await _parsedWriter.WriteAsync(
                        entry.DictionaryEntryId,
                        parsed,
                        parentParsedId: null,
                        ct);

                    senseCount++;
                }
            }

            _logger.LogInformation(
                "Parsed definitions completed | Source={SourceCode} | Senses={Count}",
                sourceCode,
                senseCount);
        }
    }
}
