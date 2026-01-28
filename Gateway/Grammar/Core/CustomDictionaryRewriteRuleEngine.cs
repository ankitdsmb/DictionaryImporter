using DictionaryImporter.Gateway.Grammar.Engines;

namespace DictionaryImporter.Gateway.Grammar.Core;

public sealed class CustomDictionaryRewriteRuleEngine(CustomRuleEngine engine) : ICustomDictionaryRewriteRuleEngine
{
    public CustomRuleEngine Engine { get; } = engine;
}