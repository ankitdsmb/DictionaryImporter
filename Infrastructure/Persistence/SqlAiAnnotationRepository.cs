namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlAiAnnotationRepository : IAiAnnotationRepository
    {
        private readonly string _connectionString;

        public SqlAiAnnotationRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DictionaryImporter")
                ?? throw new InvalidOperationException("Missing connection string: DictionaryImporter");
        }

        public async Task<IReadOnlyList<AiDefinitionCandidate>> GetDefinitionCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (@Take)
       pd.Id AS ParsedDefinitionId,
       pd.DefinitionText AS DefinitionText
FROM DictionaryEntryParsedDefinition pd
LEFT JOIN DictionaryEntryAiAnnotation ai
       ON ai.ParsedDefinitionId = pd.Id
      AND ai.SourceCode = @SourceCode
WHERE pd.SourceCode = @SourceCode
  AND ai.Id IS NULL
  AND pd.DefinitionText IS NOT NULL
  AND LTRIM(RTRIM(pd.DefinitionText)) <> ''
ORDER BY pd.Id ASC;
";

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

            const string sql = @"
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
";

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
    }
}