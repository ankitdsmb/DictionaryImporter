namespace DictionaryImporter.AI.Core.Models;

public class DeepAiQuotaExceededException : Exception
{
    public DeepAiQuotaExceededException()
    { }

    public DeepAiQuotaExceededException(string message) : base(message)
    {
    }

    public DeepAiQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}