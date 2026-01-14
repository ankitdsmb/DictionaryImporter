namespace DictionaryImporter.AI.Core.Models;

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