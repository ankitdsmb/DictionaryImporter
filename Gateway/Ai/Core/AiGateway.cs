using DictionaryImporter.Gateway.Ai.Abstractions;

namespace DictionaryImporter.Gateway.Ai.Core
{
    public sealed class AiGateway(
        IEnumerable<IAiProviderClient> providers,
        IAiResultMerger merger,
        IAiProviderSelector selector,
        ILogger<AiGateway> logger)
        : IAiGateway
    {
        private readonly List<IAiProviderClient> _providers = providers.ToList();

        public async Task<AiGatewayResponse> ExecuteAsync(AiGatewayRequest request, CancellationToken ct)
        {
            var supported = _providers.Where(p => p.Supports(request.Capability)).ToList();

            if (supported.Count == 0)
            {
                return new AiGatewayResponse
                {
                    IsSuccess = false,
                    Error = $"No provider supports capability: {request.Capability}"
                };
            }

            var chosen = selector.Select(supported, request);

            return request.Options.Mode switch
            {
                AiExecutionMode.SingleBest => await ExecuteSingleAsync(request, chosen, ct),
                AiExecutionMode.Fallback => await ExecuteFallbackAsync(request, chosen, ct),
                AiExecutionMode.Parallel => await ExecuteParallelAsync(request, chosen, ct),
                AiExecutionMode.Consensus => await ExecuteConsensusAsync(request, chosen, ct),
                _ => await ExecuteSingleAsync(request, chosen, ct)
            };
        }

        public async Task<IReadOnlyList<AiGatewayResponse>> ExecuteBulkAsync(
            IReadOnlyList<AiGatewayRequest> requests,
            CancellationToken ct)
        {
            if (requests.Count == 0)
                return Array.Empty<AiGatewayResponse>();

            var batchSize = Math.Max(1, requests[0].Options.BulkBatchSize);

            var results = new List<AiGatewayResponse>(requests.Count);

            foreach (var batch in requests.Chunk(batchSize))
            {
                var tasks = batch.Select(r => ExecuteAsync(r, ct));
                var responses = await Task.WhenAll(tasks);
                results.AddRange(responses);
            }

            return results;
        }

        private async Task<AiGatewayResponse> ExecuteSingleAsync(
            AiGatewayRequest request,
            List<IAiProviderClient> providers,
            CancellationToken ct)
        {
            var first = providers[0];
            var r = await first.ExecuteAsync(request, ct);

            return ToGatewayResponse(r, new List<AiProviderResult> { r });
        }

        private async Task<AiGatewayResponse> ExecuteFallbackAsync(
            AiGatewayRequest request,
            List<IAiProviderClient> providers,
            CancellationToken ct)
        {
            var all = new List<AiProviderResult>();

            foreach (var p in providers)
            {
                var r = await p.ExecuteAsync(request, ct);
                all.Add(r);

                if (r.Success)
                    return ToGatewayResponse(r, all);
            }

            return new AiGatewayResponse
            {
                IsSuccess = false,
                ProviderResults = all,
                Error = "All providers failed in fallback mode."
            };
        }

        private async Task<AiGatewayResponse> ExecuteParallelAsync(
            AiGatewayRequest request,
            List<IAiProviderClient> providers,
            CancellationToken ct)
        {
            var toCall = providers.Take(Math.Max(1, request.Options.ParallelCalls)).ToList();

            var tasks = toCall.Select(p => p.ExecuteAsync(request, ct));
            var results = (await Task.WhenAll(tasks)).ToList();

            var win = results.FirstOrDefault(x => x.Success);

            return new AiGatewayResponse
            {
                IsSuccess = win != null,
                OutputText = win?.Text,
                OutputBytes = win?.Bytes,
                OutputMimeType = win?.MimeType,
                ProviderResults = results,
                FinalProvider = win?.Provider,
                FinalModel = win?.Model,
                Error = win == null ? "No provider succeeded." : null
            };
        }

        private async Task<AiGatewayResponse> ExecuteConsensusAsync(
            AiGatewayRequest request,
            List<IAiProviderClient> providers,
            CancellationToken ct)
        {
            var toCall = providers.Take(Math.Max(1, request.Options.ParallelCalls)).ToList();

            var tasks = toCall.Select(p => p.ExecuteAsync(request, ct));
            var results = (await Task.WhenAll(tasks)).ToList();

            var merged = merger.Merge(request, results);

            if (!merged.Success)
            {
                return new AiGatewayResponse
                {
                    IsSuccess = false,
                    ProviderResults = results,
                    Error = merged.Error ?? "Consensus merge failed."
                };
            }

            return new AiGatewayResponse
            {
                IsSuccess = true,
                OutputText = merged.Text,
                OutputBytes = merged.Bytes,
                OutputMimeType = merged.MimeType,
                ProviderResults = results,
                FinalProvider = merged.Provider,
                FinalModel = merged.Model
            };
        }

        private static AiGatewayResponse ToGatewayResponse(AiProviderResult final, List<AiProviderResult> all)
        {
            return new AiGatewayResponse
            {
                IsSuccess = final.Success,
                OutputText = final.Text,
                OutputBytes = final.Bytes,
                OutputMimeType = final.MimeType,
                ProviderResults = all,
                FinalProvider = final.Provider,
                FinalModel = final.Model,
                Error = final.Error
            };
        }
    }
}