namespace DictionaryImporter.Infrastructure.Graph
{
    public enum GraphRebuildMode
    {
        Append,     // default, idempotent
        Rebuild     // delete + rebuild (safe)
    }
}