namespace DictionaryImporter.AI.Core.Exceptions;

public class NlpCloudRateLimitException : Exception
{
    public NlpCloudRateLimitException()
    { }

    public NlpCloudRateLimitException(string message) : base(message)
    {
    }

    public NlpCloudRateLimitException(string message, Exception inner) : base(message, inner)
    {
    }
}