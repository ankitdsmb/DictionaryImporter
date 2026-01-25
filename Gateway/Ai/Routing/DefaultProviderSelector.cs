using DictionaryImporter.Gateway.Ai.Abstractions;

namespace DictionaryImporter.Gateway.Ai.Routing;

public sealed class DefaultProviderSelector : IAiProviderSelector
{
    public List<IAiProviderClient> Select(List<IAiProviderClient> supported, AiGatewayRequest request)
    {
        // If request specifies ProviderName, use it first
        if (!string.IsNullOrWhiteSpace(request.ProviderName))
        {
            var named = supported
                .Where(p => p.Name.Equals(request.ProviderName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (named.Count > 0)
                return named.Concat(supported.Where(p => !named.Contains(p))).ToList();
        }

        // Default: keep in registration order
        return supported;
    }
}