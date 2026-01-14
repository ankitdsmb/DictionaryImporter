namespace DictionaryImporter.AI.Core.Exceptions;

public class ElevenLabsQuotaExceededException : Exception
{
    public ElevenLabsQuotaExceededException()
    { }

    public ElevenLabsQuotaExceededException(string message) : base(message)
    {
    }

    public ElevenLabsQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}