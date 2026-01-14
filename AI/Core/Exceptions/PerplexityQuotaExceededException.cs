namespace DictionaryImporter.AI.Core.Exceptions;

public class PerplexityQuotaExceededException : Exception
{
    public PerplexityQuotaExceededException()
    { }

    public PerplexityQuotaExceededException(string message) : base(message)
    {
    }

    public PerplexityQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}