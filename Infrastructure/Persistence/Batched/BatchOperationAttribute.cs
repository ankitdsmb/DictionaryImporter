using System;

namespace DictionaryImporter.Infrastructure.Persistence.Batched
{
    /// <summary>
    /// Attribute to control batching behavior for SQL operations
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class BatchOperationAttribute : Attribute
    {
        /// <summary>
        /// Maximum batch size for this operation
        /// </summary>
        public int MaxBatchSize { get; set; } = 1000;

        /// <summary>
        /// Maximum wait time in milliseconds before flushing
        /// </summary>
        public int MaxWaitMs { get; set; } = 2000;

        /// <summary>
        /// Operation key for grouping similar operations
        /// </summary>
        public string? OperationKey { get; set; }

        /// <summary>
        /// Whether to enable batching (default: true)
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Attribute to mark methods that should bypass batching
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class NoBatchAttribute : Attribute
    {
    }

    /// <summary>
    /// Attribute to mark methods that should force immediate execution
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class ImmediateExecuteAttribute : Attribute
    {
    }
}