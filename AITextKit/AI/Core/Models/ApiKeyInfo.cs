namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class ApiKeyInfo
{
    public string ProviderName { get; set; }
    public string KeyIdentifier { get; set; }
    public string KeyType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public string DeactivationReason { get; set; }
    public bool IsActive { get; set; }
}