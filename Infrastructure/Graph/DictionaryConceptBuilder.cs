using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryConceptBuilder
    {
        private readonly string _connectionString;

        public DictionaryConceptBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task BuildAsync(
            string sourceCode,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // --------------------------------------------------
            // 1. SELECT CANDIDATE SENSES
            // --------------------------------------------------
            var senses = await conn.QueryAsync<ConceptSeed>(
                    """
                    SELECT
                        p.DictionaryEntryParsedId,
                        LOWER(ISNULL(p.Domain, 'gen'))       AS Domain,
                        LOWER(ISNULL(e.PartOfSpeech, 'unk')) AS PartOfSpeech,
                        LOWER(e.NormalizedWord)              AS Head
                    FROM dbo.DictionaryEntryParsed p
                    JOIN dbo.DictionaryEntry e
                        ON e.DictionaryEntryId = p.DictionaryEntryId
                    WHERE e.SourceCode = @SourceCode;
                    """,
                    new { SourceCode = sourceCode });

            foreach (var s in senses)
            {
                ct.ThrowIfCancellationRequested();

                var conceptKey =
                    $"{s.Domain}:{s.PartOfSpeech}:{s.Head}";

                await using var tx = await conn.BeginTransactionAsync(ct);

                // --------------------------------------------------
                // 2. ENSURE CONCEPT EXISTS (SAFE)
                // --------------------------------------------------
                var conceptId =
                    await conn.ExecuteScalarAsync<long>(
                        """
                        DECLARE @Id bigint;

                        SELECT @Id = ConceptId
                        FROM dbo.Concept
                        WHERE ConceptKey = @Key;

                        IF @Id IS NULL
                        BEGIN
                            INSERT INTO dbo.Concept
                                (ConceptKey, Domain, PartOfSpeech, CreatedUtc)
                            VALUES
                                (@Key, @Domain, @Pos, SYSUTCDATETIME());

                            SET @Id = SCOPE_IDENTITY();
                        END

                        SELECT @Id;
                        """,
                        new
                        {
                            Key = conceptKey,
                            Domain = s.Domain,
                            Pos = s.PartOfSpeech
                        },
                        tx);

                // --------------------------------------------------
                // 3. ENSURE CONCEPT NODE EXISTS
                // --------------------------------------------------
                await conn.ExecuteAsync(
                    """
                    INSERT INTO dbo.GraphNode
                        (NodeId, NodeType, RefId, CreatedUtc)
                    SELECT
                        CONCAT('Concept:', @ConceptId),
                        'Concept',
                        @ConceptId,
                        SYSUTCDATETIME()
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphNode
                        WHERE NodeId = CONCAT('Concept:', @ConceptId)
                    );
                    """,
                    new { ConceptId = conceptId },
                    tx);

                // --------------------------------------------------
                // 4. LINK SENSE → CONCEPT
                // --------------------------------------------------
                await conn.ExecuteAsync(
                    """
                    INSERT INTO dbo.GraphEdge
                        (FromNodeId, ToNodeId, RelationType, CreatedUtc)
                    SELECT
                        CONCAT('Sense:', @SenseId),
                        CONCAT('Concept:', @ConceptId),
                        'BELONGS_TO',
                        SYSUTCDATETIME()
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphEdge g
                        WHERE g.FromNodeId   = CONCAT('Sense:', @SenseId)
                          AND g.ToNodeId     = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                    );
                    """,
                    new
                    {
                        SenseId = s.DictionaryEntryParsedId,
                        ConceptId = conceptId
                    },
                    tx);

                await tx.CommitAsync(ct);
            }
        }

        private sealed class ConceptSeed
        {
            public long DictionaryEntryParsedId { get; init; }
            public string Domain { get; init; } = null!;
            public string PartOfSpeech { get; init; } = null!;
            public string Head { get; init; } = null!;
        }
    }
}