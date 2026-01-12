namespace DictionaryImporter.Infrastructure.OneTimeTasks;

/// <summary>
///     One-time migration:
///     Moves editorial / non-IPA text out of CanonicalWordPronunciation
///     into CanonicalWordPronunciationNote.
/// </summary>
public sealed class EditorialIpaMigrationTask : IOneTimeDatabaseTask
{
    private readonly string _connectionString;

    public EditorialIpaMigrationTask(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string Name => "migrate-editorial-ipa";

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(
            "EXEC dbo.migrate_EditorialIpaToNotes",
            commandTimeout: 0);
    }
}