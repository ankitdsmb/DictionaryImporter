namespace DictionaryImporter.AI.Core.Exceptions;

public class TextCortexQuotaExceededException : Exception
{
    public TextCortexQuotaExceededException()
    { }

    public TextCortexQuotaExceededException(string message) : base(message)
    {
    }

    public TextCortexQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}