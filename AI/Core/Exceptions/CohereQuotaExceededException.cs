namespace DictionaryImporter.AI.Core.Exceptions;

public class CohereQuotaExceededException : Exception
{
    public CohereQuotaExceededException()
    { }

    public CohereQuotaExceededException(string message) : base(message)
    {
    }

    public CohereQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}