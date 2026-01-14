namespace DictionaryImporter.AI.Core.Exceptions;

public class WatsonQuotaExceededException : Exception
{
    public WatsonQuotaExceededException()
    { }

    public WatsonQuotaExceededException(string message) : base(message)
    {
    }

    public WatsonQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}