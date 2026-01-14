namespace DictionaryImporter.AI.Core.Exceptions;

public class AlephAlphaQuotaExceededException : Exception
{
    public AlephAlphaQuotaExceededException()
    { }

    public AlephAlphaQuotaExceededException(string message) : base(message)
    {
    }

    public AlephAlphaQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}