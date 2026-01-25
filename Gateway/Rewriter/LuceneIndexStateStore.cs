namespace DictionaryImporter.Gateway.Rewriter;

internal static class LuceneIndexStateStore
{
    private const string FileName = "_index_state.json";

    public static string GetStateFilePath(string indexPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
            return FileName;

        return Path.Combine(indexPath, FileName);
    }

    public static LuceneIndexState Load(string indexPath)
    {
        try
        {
            var path = GetStateFilePath(indexPath);
            if (!File.Exists(path))
                return new LuceneIndexState();

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new LuceneIndexState();

            var state = JsonSerializer.Deserialize<LuceneIndexState>(json);
            return state ?? new LuceneIndexState();
        }
        catch
        {
            return new LuceneIndexState();
        }
    }

    public static void Save(string indexPath, LuceneIndexState state)
    {
        try
        {
            if (state is null)
                return;

            Directory.CreateDirectory(indexPath);

            var path = GetStateFilePath(indexPath);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(path, json);
        }
        catch
        {
            // never crash
        }
    }
}