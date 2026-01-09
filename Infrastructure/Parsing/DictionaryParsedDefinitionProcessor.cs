using Dapper;
using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing
{
    public sealed class DictionaryParsedDefinitionProcessor
    {
        private readonly string _connectionString;
        private readonly IDictionaryDefinitionParser _parser;
        private readonly SqlParsedDefinitionWriter _parsedWriter;
        private readonly SqlDictionaryEntryCrossReferenceWriter _crossRefWriter;
        private readonly SqlDictionaryAliasWriter _aliasWriter;
        private readonly IEntryEtymologyWriter _etymologyWriter;
        private readonly SqlDictionaryEntryVariantWriter _variantWriter;
        private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;

        public DictionaryParsedDefinitionProcessor(
            string connectionString,
            IDictionaryDefinitionParser parser,
            SqlParsedDefinitionWriter parsedWriter,
            SqlDictionaryEntryCrossReferenceWriter crossRefWriter,
            SqlDictionaryAliasWriter aliasWriter,
            IEntryEtymologyWriter etymologyWriter,
            SqlDictionaryEntryVariantWriter variantWriter,
            ILogger<DictionaryParsedDefinitionProcessor> logger)
        {
            _connectionString = connectionString;
            _parser = parser;
            _parsedWriter = parsedWriter;
            _crossRefWriter = crossRefWriter;
            _aliasWriter = aliasWriter;
            _etymologyWriter = etymologyWriter;
            _variantWriter = variantWriter;
            _logger = logger;
        }

        public async Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Stage=Parsing started | Source={SourceCode}",
                sourceCode);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // --------------------------------------------------
            // 1. Load DictionaryEntry rows
            // --------------------------------------------------
            var entries =
                (await conn.QueryAsync<DictionaryEntry>(
                    """
                    SELECT *
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode
                      AND Definition IS NOT NULL
                    """,
                    new { SourceCode = sourceCode }))
                .ToList();

            _logger.LogInformation(
                "Stage=Parsing | EntriesLoaded={Count} | Source={SourceCode}",
                entries.Count,
                sourceCode);

            int entryIndex = 0;
            int senseCount = 0;
            int crossRefCount = 0;
            int aliasCount = 0;
            int variantCount = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                entryIndex++;

                // --------------------------------------------------
                // Progress heartbeat (every 1k entries)
                // --------------------------------------------------
                if (entryIndex % 1_000 == 0)
                {
                    _logger.LogInformation(
                        "Stage=Parsing progress | Source={SourceCode} | Entries={Processed}/{Total} | Senses={Senses}",
                        sourceCode,
                        entryIndex,
                        entries.Count,
                        senseCount);
                }

                // --------------------------------------------------
                // 2. Parse entry into ParsedDefinition objects
                // --------------------------------------------------
                var parsedDefinitions =
                    _parser.Parse(entry);

                if (parsedDefinitions == null)
                    continue;

                // --------------------------------------------------
                // 3. Persist parsed definitions
                // --------------------------------------------------
                foreach (var parsed in parsedDefinitions)
                {
                    // 3.1 Write parsed sense
                    var parsedId =
                        await _parsedWriter.WriteAsync(
                            entry.DictionaryEntryId,
                            parsed,
                            parentParsedId: null,
                            ct);

                    senseCount++;

                    // 3.2 Cross-references
                    foreach (var cr in parsed.CrossReferences)
                    {
                        await _crossRefWriter.WriteAsync(
                            parsedId,
                            cr,
                            ct);

                        crossRefCount++;
                    }

                    // 3.3 Alias
                    if (!string.IsNullOrWhiteSpace(parsed.Alias))
                    {
                        await _aliasWriter.WriteAsync(
                            parsedId,
                            parsed.Alias,
                            ct);

                        aliasCount++;
                    }

                    // 3.4 Etymology (entry-level)
                    if (!string.IsNullOrWhiteSpace(entry.Etymology))
                    {
                        await _etymologyWriter.WriteAsync(
                            new DictionaryEntryEtymology
                            {
                                DictionaryEntryId = entry.DictionaryEntryId,
                                EtymologyText = entry.Etymology,
                                CreatedUtc = DateTime.UtcNow
                            },
                            ct);
                    }
                }

                // --------------------------------------------------
                // 4. Headword variants (entry-level)
                // --------------------------------------------------
                foreach (var (variant, type)
                    in Sources.Gutenberg.Parsing.WebsterHeadwordVariantGenerator
                        .Generate(entry.Word))
                {
                    await _variantWriter.WriteAsync(
                        entry.DictionaryEntryId,
                        variant,
                        type,
                        ct);

                    variantCount++;
                }
            }

            _logger.LogInformation(
                "Stage=Parsing completed | Source={SourceCode} | Entries={Entries} | Senses={Senses} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants}",
                sourceCode,
                entryIndex,
                senseCount,
                crossRefCount,
                aliasCount,
                variantCount);
        }
    }
}
