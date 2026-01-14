namespace DictionaryImporter.AI.Core.Exceptions;

public class CohereRateLimitException : Exception
{
    public CohereRateLimitException()
    { }

    public CohereRateLimitException(string message) : base(message)
    {
    }

    public CohereRateLimitException(string message, Exception inner) : base(message, inner)
    {
    }
}