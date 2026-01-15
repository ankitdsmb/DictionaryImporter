namespace DictionaryImporter.AI.Core.Models;

public class RequestContext
{
    public string UserId { get; set; }
    public string SessionId { get; set; }
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string Application { get; set; } = "DictionaryImporter";
    public string Language { get; set; } = "en";
    public Dictionary<string, string> Tags { get; set; } = new();
    public Priority Priority { get; set; } = Priority.Normal;
}