# DictionaryImporter.AiGateway

A generic, config-driven AI Gateway library for the DictionaryImporter solution.

## What you get
1. One place AI API: `IAiGateway`
2. Supports any provider via:
   - BaseUrl
   - ApiKey
   - RequestTemplate JSON
   - ResponsePath JSONPath
3. Execution modes:
   - SingleBest
   - Fallback
   - Parallel
   - Consensus
4. Bulk execution support

## Quick usage

### 1) Register
```csharp
services.AddDictionaryImporterAiGateway(configuration);
```

### 2) appsettings.json sample
```json
{
  "AiGateway": {
    "Providers": [
      {
        "Name": "OpenAI",
        "BaseUrl": "https://api.openai.com/v1/chat/completions",
        "ApiKey": "YOUR_KEY",
        "AuthHeader": "Authorization: Bearer {{ApiKey}}",
        "DefaultModel": "gpt-4o-mini",
        "RequestTemplate": {
          "model": "{{Model}}",
          "messages": [
            { "role": "system", "content": "{{SystemPrompt}}" },
            { "role": "user", "content": "{{Prompt}}" }
          ],
          "temperature": "{{Temperature}}",
          "max_tokens": "{{MaxTokens}}"
        },
        "ResponsePath": "$.choices[0].message.content"
      },
      {
        "Name": "Ollama(Local)",
        "BaseUrl": "http://localhost:11434/api/generate",
        "ApiKey": "",
        "AuthHeader": "",
        "DefaultModel": "llama3.1",
        "RequestTemplate": {
          "model": "{{Model}}",
          "prompt": "{{Prompt}}",
          "stream": false
        },
        "ResponsePath": "$.response"
      }
    ]
  }
}
```

### 3) Call from pipeline
```csharp
var res = await aiGateway.ExecuteAsync(new AiGatewayRequest
{
    Task = AiTaskType.RewriteDefinition,
    Capability = AiCapability.Text,
    Prompt = "Rewrite the definition in clean English...",
    Options = new AiExecutionOptions
    {
        Mode = AiExecutionMode.Fallback,
        ParallelCalls = 2
    }
}, ct);

var text = res.OutputText;
```

## Notes
- This library is intentionally generic. Each provider is integrated by **template + JSONPath**, not by custom SDK code.
- For image/audio/video providers, define templates + response paths according to the vendor API.
