namespace DictionaryImporter.AI.Core.Exceptions;

public class Ai21QuotaExceededException : Exception
{
    public Ai21QuotaExceededException()
    { }

    public Ai21QuotaExceededException(string message) : base(message)
    {
    }

    public Ai21QuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}