namespace DictionaryImporter.Sources.StructuredJson
{
    public sealed class StructuredJsonTransformer
        : IDataTransformer<StructuredJsonRawEntry>
    {
        public IEnumerable<DictionaryEntry> Transform(
            StructuredJsonRawEntry raw)
        {
            yield return new DictionaryEntry
            {
                SourceCode = "STRUCT_JSON",
                Word = raw.Word,
                NormalizedWord = raw.NormalizedWord,
                Definition = raw.Definition,
                PartOfSpeech = raw.PartOfSpeech,
                SenseNumber = raw.SenseNumber
            };
        }
    }
}