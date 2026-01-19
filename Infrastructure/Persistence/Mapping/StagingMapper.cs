namespace DictionaryImporter.Infrastructure.Persistence.Mapping
{
    public static class StagingMapper
    {
        public static DictionaryEntryStaging Map(DictionaryEntry e)
        {
            return new DictionaryEntryStaging
            {
                Word = e.Word,
                NormalizedWord = e.NormalizedWord,
                PartOfSpeech = e.PartOfSpeech,
                Definition = e.Definition,
                Etymology = e.Etymology,
                RawFragment = e.RawFragment,
                SenseNumber = e.SenseNumber,
                SourceCode = e.SourceCode,
                CreatedUtc = e.CreatedUtc
            };
        }
    }
}