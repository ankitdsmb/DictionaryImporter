namespace DictionaryImporter.Infrastructure.OneTimeTasks
{
    /// <summary>
    ///     One-time migration:
    ///     Moves editorial / non-IPA text out of CanonicalWordPronunciation
    ///     into CanonicalWordPronunciationNote.
    /// </summary>
    public sealed class EditorialIpaMigrationTask(string connectionString) : IOneTimeDatabaseTask
    {
        public string Name => "migrate-editorial-ipa";

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await conn.ExecuteAsync(
                "EXEC dbo.migrate_EditorialIpaToNotes",
                commandTimeout: 0);
        }
    }
}