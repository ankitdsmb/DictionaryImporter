namespace DictionaryImporter.AI.Core.Exceptions;

public class AlephAlphaVisionQuotaExceededException : Exception
{
    public AlephAlphaVisionQuotaExceededException()
    { }

    public AlephAlphaVisionQuotaExceededException(string message) : base(message)
    {
    }

    public AlephAlphaVisionQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}