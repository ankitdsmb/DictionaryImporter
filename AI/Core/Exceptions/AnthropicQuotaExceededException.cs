namespace DictionaryImporter.AI.Core.Exceptions;

public class AnthropicQuotaExceededException : Exception
{
    public AnthropicQuotaExceededException()
    { }

    public AnthropicQuotaExceededException(string message) : base(message)
    {
    }

    public AnthropicQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}