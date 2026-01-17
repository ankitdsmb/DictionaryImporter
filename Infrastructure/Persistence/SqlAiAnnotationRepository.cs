namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlAiAnnotationRepository(IConfiguration configuration) : IAiAnnotationRepository
    {
        private readonly string _connectionString = configuration.GetConnectionString("DictionaryImporter")
                                                    ?? throw new InvalidOperationException("Missing connection string: DictionaryImporter");

        public async Task<IReadOnlyList<AiDefinitionCandidate>> GetDefinitionCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct)
        {
            const string sql = """
                               SELECT TOP (@Take)
                                      pd.DictionaryEntryParsedId AS ParsedDefinitionId,
                                      pd.Definition AS DefinitionText
                               FROM DictionaryEntryParsed pd
                               LEFT JOIN DictionaryEntryAiAnnotation ai
                                      ON ai.ParsedDefinitionId = pd.DictionaryEntryParsedId
                                     AND ai.SourceCode = @SourceCode
                               WHERE ai.ParsedDefinitionId IS NULL
                                 AND pd.Definition IS NOT NULL
                                 AND LTRIM(RTRIM(pd.Definition)) <> ''
                               ORDER BY pd.DictionaryEntryParsedId ASC;
                               """;

            await using var conn = new SqlConnection(_connectionString);

            var result = await conn.QueryAsync<AiDefinitionCandidate>(
                new CommandDefinition(sql, new { SourceCode = sourceCode, Take = take }, cancellationToken: ct));

            return result.ToList();
        }

        public async Task SaveAiEnhancementsAsync(
            string sourceCode,
            IReadOnlyList<AiDefinitionEnhancement> enhancements,
            CancellationToken ct)
        {
            if (enhancements is null || enhancements.Count == 0)
                return;

            const string sql = """

                               IF NOT EXISTS
                               (
                                   SELECT 1
                                   FROM DictionaryEntryAiAnnotation
                                   WHERE SourceCode = @SourceCode
                                     AND ParsedDefinitionId = @ParsedDefinitionId
                               )
                               BEGIN
                                   INSERT INTO DictionaryEntryAiAnnotation
                                   (
                                       SourceCode,
                                       ParsedDefinitionId,
                                       OriginalDefinition,
                                       AiEnhancedDefinition,
                                       AiNotesJson,
                                       Provider,
                                       Model,
                                       CreatedUtc
                                   )
                                   VALUES
                                   (
                                       @SourceCode,
                                       @ParsedDefinitionId,
                                       @OriginalDefinition,
                                       @AiEnhancedDefinition,
                                       @AiNotesJson,
                                       @Provider,
                                       @Model,
                                       SYSUTCDATETIME()
                                   );
                               END

                               """;

            await using var conn = new SqlConnection(_connectionString);

            var rows = enhancements.Select(x => new
            {
                SourceCode = sourceCode,
                x.ParsedDefinitionId,
                x.OriginalDefinition,
                x.AiEnhancedDefinition,
                x.AiNotesJson,
                x.Provider,
                x.Model
            });

            await conn.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: ct));
        }

        public async Task SaveAiEnhancementAsync(
            AiDefinitionEnhancement enhancement,
            CancellationToken ct)
        {
            const string sql = """
                               MERGE dbo.AiDefinitionEnhancements AS T
                               USING (SELECT @ParsedDefinitionId AS ParsedDefinitionId) AS S
                               ON T.ParsedDefinitionId = S.ParsedDefinitionId

                               WHEN MATCHED THEN
                                   UPDATE SET
                                       OriginalDefinition   = @OriginalDefinition,
                                       AiEnhancedDefinition = @AiEnhancedDefinition,
                                       AiNotesJson          = @AiNotesJson,
                                       Provider             = @Provider,
                                       Model                = @Model,
                                       UpdatedOnUtc         = DATETIMEPICKER()

                               WHEN NOT MATCHED THEN
                                   INSERT
                                   (
                                       ParsedDefinitionId,
                                       OriginalDefinition,
                                       AiEnhancedDefinition,
                                       AiNotesJson,
                                       Provider,
                                       Model,
                                       CreatedOnUtc,
                                       UpdatedOnUtc
                                   )
                                   VALUES
                                   (
                                       @ParsedDefinitionId,
                                       @OriginalDefinition,
                                       @AiEnhancedDefinition,
                                       @AiNotesJson,
                                       @Provider,
                                       @Model,
                                       DATETIMEPICKER(),
                                       DATETIMEPICKER()
                                   );
                               """;

            using var con = new SqlConnection(_connectionString);
            await con.ExecuteAsync(new CommandDefinition(sql, enhancement, cancellationToken: ct));
        }

        public async Task<bool> AiEnhancementExistsAsync(long parsedDefinitionId, CancellationToken ct)
        {
            const string sql = """
                               SELECT CASE WHEN EXISTS
                               (
                                   SELECT 1
                                   FROM dbo.AiDefinitionEnhancements
                                   WHERE ParsedDefinitionId = @ParsedDefinitionId
                               )
                               THEN CAST(1 AS BIT)
                               ELSE CAST(0 AS BIT)
                               END
                               """;

            using var con = new SqlConnection(_connectionString);
            return await con.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { ParsedDefinitionId = parsedDefinitionId }, cancellationToken: ct));
        }
    }
}