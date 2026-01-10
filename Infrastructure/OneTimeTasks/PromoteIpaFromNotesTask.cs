using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.OneTimeTasks
{
    /// <summary>
    /// Promotes real IPA tokens from pronunciation notes
    /// into CanonicalWordPronunciation.
    /// Safe to re-run.
    /// </summary>
    public sealed class PromoteIpaFromNotesTask : IOneTimeDatabaseTask
    {
        public string Name => "promote-ipa-from-notes";

        private readonly string _connectionString;

        public PromoteIpaFromNotesTask(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await conn.ExecuteAsync(
                "EXEC dbo.promote_IpaFromPronunciationNotes",
                commandTimeout: 0);
        }
    }
}
