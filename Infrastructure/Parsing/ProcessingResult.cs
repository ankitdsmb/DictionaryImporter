namespace DictionaryImporter.Infrastructure.Parsing;

public class ProcessingResult
{
    public int TotalEntries { get; set; }
    public int ParsedInserted { get; set; }
    public int CrossRefInserted { get; set; }
    public int AliasInserted { get; set; }
    public int ExampleInserted { get; set; }
    public int SynonymInserted { get; set; }
    public int EtymologyExtracted { get; set; }
    public int IpaExtracted { get; set; }
    public int AudioExtracted { get; set; }

    public void Merge(ProcessingResult other)
    {
        CrossRefInserted += other.CrossRefInserted;
        AliasInserted += other.AliasInserted;
        ExampleInserted += other.ExampleInserted;
        SynonymInserted += other.SynonymInserted;
        EtymologyExtracted += other.EtymologyExtracted;
        IpaExtracted += other.IpaExtracted;
        AudioExtracted += other.AudioExtracted;
    }
}