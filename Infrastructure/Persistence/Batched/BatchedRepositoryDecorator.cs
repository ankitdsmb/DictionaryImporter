using Microsoft.Extensions.Logging;
using System.Reflection;

namespace DictionaryImporter.Infrastructure.Persistence.Batched
{
    /// <summary>
    /// Simple decorator for batching (alternative to AOP)
    /// </summary>
    public class BatchedRepositoryDecorator<T> : DispatchProxy
    {
        private T _decorated = default!;
        private GenericSqlBatcher _batcher = default!;
        private ILogger _logger = default!;

        public static T Create(T decorated, GenericSqlBatcher batcher, ILogger logger)
        {
            object proxy = Create<T, BatchedRepositoryDecorator<T>>();
            ((BatchedRepositoryDecorator<T>)proxy).SetParameters(decorated, batcher, logger);
            return (T)proxy;
        }

        private void SetParameters(T decorated, GenericSqlBatcher batcher, ILogger logger)
        {
            _decorated = decorated;
            _batcher = batcher;
            _logger = logger;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null)
                return null;

            try
            {
                // Check if method should be batched
                var methodName = targetMethod.Name.ToUpperInvariant();
                var returnType = targetMethod.ReturnType;

                // Auto-detect write operations
                if (methodName.Contains("INSERT") ||
                    methodName.Contains("UPDATE") ||
                    methodName.Contains("DELETE") ||
                    methodName.Contains("WRITE") ||
                    methodName.Contains("SAVE"))
                {
                    _logger.LogTrace("Intercepting write operation: {Method}", targetMethod.Name);

                    // Execute the method
                    var result = targetMethod.Invoke(_decorated, args);

                    // If it returns a Task, we need to handle it specially
                    if (result is Task task)
                    {
                        return InterceptAsync(task, targetMethod.Name);
                    }

                    return result;
                }

                // Execute non-batched operations normally
                return targetMethod.Invoke(_decorated, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private async Task InterceptAsync(Task originalTask, string methodName)
        {
            try
            {
                await originalTask;

                // After async operation completes, trigger batch flush
                await _batcher.FlushAllAsync();
                _logger.LogTrace("Batch flushed after {Method}", methodName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in intercepted method {Method}", methodName);
                throw;
            }
        }
    }
}