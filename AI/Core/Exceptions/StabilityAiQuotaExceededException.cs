namespace DictionaryImporter.AI.Core.Exceptions;

public class StabilityAiQuotaExceededException : Exception
{
    public StabilityAiQuotaExceededException()
    { }

    public StabilityAiQuotaExceededException(string message) : base(message)
    {
    }

    public StabilityAiQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}