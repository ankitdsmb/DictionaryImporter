namespace DictionaryImporter.AITextKit.AI.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ProviderAttribute : Attribute
{
    public string Name { get; }

    public int Priority { get; set; } = 10;

    public bool SupportsCaching { get; set; } = false;

    public bool IsReliable { get; set; } = true;

    public bool SupportsStreaming { get; set; } = false;

    public bool HasFreeTier { get; set; } = false;

    public int CostTier { get; set; } = 1;

    public ProviderAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider name cannot be null or empty", nameof(name));

        Name = name;
    }
}