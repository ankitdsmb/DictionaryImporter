using System.Net.Http.Headers;
using DictionaryImporter.Gateway.Ai.Abstractions;
using DictionaryImporter.Gateway.Ai.Configuration;
using DictionaryImporter.Gateway.Ai.Template;
using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Gateway.Ai.Providers;

public sealed class GenericHttpAiProviderClient(
    AiProviderConfig providerConfig,
    IHttpClientFactory httpClientFactory,
    ILogger<GenericHttpAiProviderClient> logger)
    : IAiProviderClient
{
    public string Name => providerConfig.Name;

    public bool Supports(AiCapability capability)
    {
        // Generic provider can be used for any capability
        // as long as its request template + response path matches the provider.
        return true;
    }

    public async Task<AiProviderResult> ExecuteAsync(AiGatewayRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var client = httpClientFactory.CreateClient("DictionaryImporter.AiGateway");

            using var http = new HttpRequestMessage(HttpMethod.Post, providerConfig.BaseUrl);

            ApplyHeaders(http, request);

            var payload = TemplateEngine.BuildPayload(providerConfig, request);

            http.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

            using var res = await client.SendAsync(http, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                return new AiProviderResult
                {
                    Provider = Name,
                    Model = request.Model ?? providerConfig.DefaultModel,
                    Success = false,
                    Error = $"HTTP {(int)res.StatusCode} ({res.StatusCode}): {raw}",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            if (string.IsNullOrWhiteSpace(providerConfig.ResponsePath))
            {
                return new AiProviderResult
                {
                    Provider = Name,
                    Model = request.Model ?? providerConfig.DefaultModel,
                    Success = true,
                    Text = raw,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            var json = JToken.Parse(raw);
            var output = json.SelectToken(providerConfig.ResponsePath)?.ToString();

            return new AiProviderResult
            {
                Provider = Name,
                Model = request.Model ?? providerConfig.DefaultModel,
                Success = !string.IsNullOrWhiteSpace(output),
                Text = output ?? string.Empty,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Generic provider {Provider} failed", Name);

            return new AiProviderResult
            {
                Provider = Name,
                Model = request.Model ?? providerConfig.DefaultModel,
                Success = false,
                Error = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private void ApplyHeaders(HttpRequestMessage http, AiGatewayRequest request)
    {
        // AuthHeader (single header line)
        if (!string.IsNullOrWhiteSpace(providerConfig.AuthHeader))
        {
            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApiKey"] = providerConfig.ApiKey ?? string.Empty
            };

            var headerLine = TemplateEngine.ApplyTokenString(providerConfig.AuthHeader, vars);

            var idx = headerLine.IndexOf(':');
            if (idx > 0)
            {
                var name = headerLine[..idx].Trim();
                var value = headerLine[(idx + 1)..].Trim();
                http.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Additional headers
        foreach (var kv in providerConfig.Headers)
        {
            var value = kv.Value.Replace("{ApiKey}", providerConfig.ApiKey ?? "");
            http.Headers.TryAddWithoutValidation(kv.Key, value);
        }

        http.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}