namespace DictionaryImporter.AI.Core.Models;

public enum ProviderSelectionStrategy
{
    PriorityBased,
    PerformanceBased,
    RoundRobin,
    Random,
    CostOptimized
}