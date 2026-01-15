using System;

namespace DictionaryImporter.AI.Core.Attributes
{
    /// <summary>
    /// Attribute to mark AI provider classes with metadata
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ProviderAttribute : Attribute
    {
        /// <summary>
        /// The name of the provider (must match configuration key)
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Priority for provider selection (lower = higher priority)
        /// </summary>
        public int Priority { get; set; } = 10;

        /// <summary>
        /// Whether this provider supports response caching
        /// </summary>
        public bool SupportsCaching { get; set; } = false;

        /// <summary>
        /// Whether this provider is considered reliable for fallback
        /// </summary>
        public bool IsReliable { get; set; } = true;

        /// <summary>
        /// Whether this provider supports streaming responses
        /// </summary>
        public bool SupportsStreaming { get; set; } = false;

        /// <summary>
        /// Whether this provider has a free tier
        /// </summary>
        public bool HasFreeTier { get; set; } = false;

        /// <summary>
        /// Cost tier of the provider (0 = free, 1 = low, 2 = medium, 3 = high)
        /// </summary>
        public int CostTier { get; set; } = 1;

        /// <summary>
        /// Initializes a new instance of the ProviderAttribute
        /// </summary>
        /// <param name="name">The provider name</param>
        public ProviderAttribute(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Provider name cannot be null or empty", nameof(name));

            Name = name;
        }
    }
}