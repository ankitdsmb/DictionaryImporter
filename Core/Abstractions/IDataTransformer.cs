namespace DictionaryImporter.Core.Abstractions
{
    public interface IDataTransformer<TRaw>
    {
        IEnumerable<DictionaryEntry> Transform(TRaw raw);
    }
}