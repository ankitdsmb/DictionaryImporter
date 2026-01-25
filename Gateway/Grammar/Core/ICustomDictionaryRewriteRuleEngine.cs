using DictionaryImporter.Gateway.Grammar.Engines;

namespace DictionaryImporter.Gateway.Grammar.Core
{
    public interface ICustomDictionaryRewriteRuleEngine
    {
        CustomRuleEngine Engine { get; }
    }

    public sealed class CustomDictionaryRewriteRuleEngine(CustomRuleEngine engine) : ICustomDictionaryRewriteRuleEngine
    {
        public CustomRuleEngine Engine { get; } = engine;
    }
}