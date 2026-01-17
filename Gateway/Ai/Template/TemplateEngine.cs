using DictionaryImporter.Gateway.Ai.Abstractions;
using DictionaryImporter.Gateway.Ai.Configuration;
using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Gateway.Ai.Template
{
    internal static class TemplateEngine
    {
        private static readonly Regex TokenRegex = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

        public static JObject BuildPayload(AiProviderConfig provider, AiGatewayRequest req)
        {
            var template = (JObject)provider.RequestTemplate.DeepClone();

            // Built-in variables
            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApiKey"] = provider.ApiKey ?? "",
                ["Model"] = !string.IsNullOrWhiteSpace(req.Model) ? req.Model! : provider.DefaultModel,
                ["Prompt"] = req.Prompt ?? req.InputText ?? "",
                ["SystemPrompt"] = req.SystemPrompt ?? "You are a helpful assistant.",
                ["Temperature"] = req.Options.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MaxTokens"] = req.Options.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Capability"] = req.Capability.ToString(),
                ["Task"] = req.Task.ToString(),
                ["Language"] = req.Language ?? "en",
                ["SourceCode"] = req.SourceCode ?? "",
                ["CorrelationId"] = req.CorrelationId ?? ""
            };

            // User variables override built-ins
            foreach (var kv in req.Variables)
                vars[kv.Key] = kv.Value;

            ReplaceTokens(template, vars);

            return template;
        }

        public static string ApplyTokenString(string input, Dictionary<string, string> vars)
        {
            return TokenRegex.Replace(input, m =>
            {
                var key = m.Groups[1].Value.Trim();
                return vars.TryGetValue(key, out var value) ? value : m.Value;
            });
        }

        private static void ReplaceTokens(JToken token, Dictionary<string, string> vars)
        {
            if (token.Type == JTokenType.Object)
            {
                foreach (var prop in token.Children<JProperty>())
                    ReplaceTokens(prop.Value, vars);

                return;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                    ReplaceTokens(child, vars);

                return;
            }

            if (token.Type == JTokenType.String)
            {
                var s = token.Value<string>() ?? string.Empty;
                var updated = ApplyTokenString(s, vars);
                token.Replace(updated);
            }
        }
    }
}