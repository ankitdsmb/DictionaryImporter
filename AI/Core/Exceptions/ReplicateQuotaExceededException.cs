namespace DictionaryImporter.AI.Core.Exceptions;

public class ReplicateQuotaExceededException : Exception
{
    public ReplicateQuotaExceededException()
    { }

    public ReplicateQuotaExceededException(string message) : base(message)
    {
    }

    public ReplicateQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}