using DictionaryImporter.Gateway.Grammar.Infrastructure;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.HostedService
{
    public sealed class StartupCleanupHostedService(GrammarStartupCleanup cleanup) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await cleanup.ExecuteAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}