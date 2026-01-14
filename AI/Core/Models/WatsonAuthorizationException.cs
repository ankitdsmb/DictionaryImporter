namespace DictionaryImporter.AI.Core.Models;

public class WatsonAuthorizationException : Exception
{
    public WatsonAuthorizationException()
    { }

    public WatsonAuthorizationException(string message) : base(message)
    {
    }

    public WatsonAuthorizationException(string message, Exception inner) : base(message, inner)
    {
    }
}