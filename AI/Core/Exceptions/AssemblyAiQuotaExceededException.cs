namespace DictionaryImporter.AI.Core.Exceptions;

public class AssemblyAiQuotaExceededException : Exception
{
    public AssemblyAiQuotaExceededException()
    { }

    public AssemblyAiQuotaExceededException(string message) : base(message)
    {
    }

    public AssemblyAiQuotaExceededException(string message, Exception inner) : base(message, inner)
    {
    }
}