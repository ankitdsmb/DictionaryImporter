namespace DictionaryImporter.AI.Core.Exceptions;

public class TogetherAiQuotaExceededException : Exception
{
    public TogetherAiQuotaExceededException()
    { }

    public TogetherAiQuotaExceededException(string message) : base(message)
    {
    }

    public TogetherAiQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}