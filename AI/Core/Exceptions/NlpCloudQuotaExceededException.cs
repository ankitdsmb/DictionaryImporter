namespace DictionaryImporter.AI.Core.Exceptions;

public class NlpCloudQuotaExceededException : Exception
{
    public NlpCloudQuotaExceededException()
    { }

    public NlpCloudQuotaExceededException(string message) : base(message)
    {
    }

    public NlpCloudQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}