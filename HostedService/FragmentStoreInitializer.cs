using DictionaryImporter.Infrastructure.FragmentStore;
using Microsoft.Extensions.Hosting;

namespace DictionaryImporter.HostedService;

public sealed class FragmentStoreInitializer(IRawFragmentStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        RawFragments.Initialize(store);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}