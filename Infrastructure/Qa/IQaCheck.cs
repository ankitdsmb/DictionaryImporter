using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Infrastructure.Qa
{
    public interface IQaCheck
    {
        string Name { get; }
        string Phase { get; }

        Task<IReadOnlyList<QaSummaryRow>> ExecuteAsync(
            CancellationToken ct);
    }
}
