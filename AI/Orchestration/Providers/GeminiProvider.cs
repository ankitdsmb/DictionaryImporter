using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public class GeminiProvider : BaseCompletionProvider
    {
        private const string DefaultModel = "gemini-pro";
        private const int FreeTierMaxTokens = 32768;

        public override string ProviderName => "Gemini";
        public override int Priority => 3;
        public override ProviderType Type => ProviderType.TextCompletion;

        public override bool SupportsAudio => false;

        public override bool SupportsVision => true;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public GeminiProvider(
            HttpClient httpClient,
            ILogger<GeminiProvider> logger,
            IOptions<ProviderConfiguration> configuration)
            : base(httpClient, logger, configuration)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("Gemini API key not configured. Provider will be disabled.");
                return;
            }

            ConfigureAuthentication();
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.ImageAnalysis = true;
            Capabilities.MaxTokensLimit = FreeTierMaxTokens;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Clear();

            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrEmpty(Configuration.ApiKey))
                {
                    throw new InvalidOperationException("Gemini API key not configured");
                }

                ValidateRequest(request);

                var payload = CreateRequestPayload(request);
                var httpRequest = CreateHttpRequest(payload);

                var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
                Logger.LogDebug("Sending request to Gemini with model {Model}", model);

                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseResponse(content, out var tokenUsage);

                stopwatch.Stop();

                return new AiResponse
                {
                    Content = result.Trim(),
                    Provider = ProviderName,
                    Model = model,
                    TokensUsed = tokenUsage,
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "free_tier", true },
                        { "google_ai", true },
                        { "supports_vision", true }
                    }
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "Gemini provider failed");

                if (ShouldFallback(ex))
                    throw;

                return new AiResponse
                {
                    Content = string.Empty,
                    Provider = ProviderName,
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    Metadata = new Dictionary<string, object>
                    {
                        { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                        { "error_type", ex.GetType().Name }
                    }
                };
            }
        }

        private void ValidateRequest(AiRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("Prompt cannot be empty");

            if (request.MaxTokens > FreeTierMaxTokens)
            {
                Logger.LogWarning(
                    "Requested {Requested} tokens exceeds Gemini free tier limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
            }
        }

        private object CreateRequestPayload(AiRequest request)
        {
            if (request.ImageData != null || request.ImageUrls?.Count > 0)
            {
                return CreateVisionPayload(request);
            }

            var contents = new List<object>
            {
                new
                {
                    parts = new[]
                    {
                        new { text = request.Prompt }
                    }
                }
            };

            return new
            {
                contents = contents,
                generationConfig = new
                {
                    maxOutputTokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                    temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                    topP = 0.95,
                    topK = 40
                },
                safetySettings = new[]
                {
                    new
                    {
                        category = "HARM_CATEGORY_HARASSMENT",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new
                    {
                        category = "HARM_CATEGORY_HATE_SPEECH",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new
                    {
                        category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new
                    {
                        category = "HARM_CATEGORY_DANGEROUS_CONTENT",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    }
                }
            };
        }

        private object CreateVisionPayload(AiRequest request)
        {
            var parts = new List<object>
            {
                new { text = request.Prompt }
            };

            if (request.ImageData != null)
            {
                var base64Image = Convert.ToBase64String(request.ImageData);
                parts.Add(new
                {
                    inlineData = new
                    {
                        mimeType = GetMimeType(request.ImageFormat),
                        data = base64Image
                    }
                });
            }
            else if (request.ImageUrls?.Count > 0)
            {
                foreach (var url in request.ImageUrls.Take(1))
                {
                    parts.Add(new
                    {
                        inlineData = new
                        {
                            mimeType = "image/jpeg",
                            data = GetBase64FromUrl(url)
                        }
                    });
                }
            }

            return new
            {
                contents = new[]
                {
                    new { parts = parts }
                },
                generationConfig = new
                {
                    maxOutputTokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                    temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                    topP = 0.95,
                    topK = 40
                },
                safetySettings = new[]
                {
                    new
                    {
                        category = "HARM_CATEGORY_HARASSMENT",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new
                    {
                        category = "HARM_CATEGORY_HATE_SPEECH",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new
                    {
                        category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new
                    {
                        category = "HARM_CATEGORY_DANGEROUS_CONTENT",
                        threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    }
                }
            };
        }

        private string GetMimeType(string imageFormat)
        {
            if (string.IsNullOrEmpty(imageFormat))
                return "image/jpeg";

            return imageFormat.ToLower() switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        private string GetBase64FromUrl(string url)
        {
            return string.Empty;
        }

        private HttpRequestMessage CreateHttpRequest(object payload)
        {
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
            var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl)
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
                : Configuration.BaseUrl;

            var url = $"{baseUrl}?key={Configuration.ApiKey}";

            return new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    }),
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;

            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);

                if (jsonDoc.RootElement.TryGetProperty("error", out var error))
                {
                    var errorMessage = error.GetProperty("message").GetString() ?? "Unknown error";
                    throw new HttpRequestException($"Gemini API error: {errorMessage}");
                }

                if (jsonDoc.RootElement.TryGetProperty("candidates", out var candidates))
                {
                    var firstCandidate = candidates.EnumerateArray().FirstOrDefault();
                    if (firstCandidate.TryGetProperty("content", out var content))
                    {
                        if (content.TryGetProperty("parts", out var parts))
                        {
                            var firstPart = parts.EnumerateArray().FirstOrDefault();
                            if (firstPart.TryGetProperty("text", out var text))
                            {
                                var resultText = text.GetString() ?? string.Empty;

                                if (jsonDoc.RootElement.TryGetProperty("usageMetadata", out var usageMetadata))
                                {
                                    tokenUsage = usageMetadata.GetProperty("totalTokenCount").GetInt64();
                                }
                                else
                                {
                                    tokenUsage = EstimateTokenUsage(resultText);
                                }

                                return resultText;
                            }
                        }
                    }
                }

                throw new FormatException("Could not find candidates in Gemini response");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse Gemini response");
                throw new FormatException("Invalid Gemini response format");
            }
        }

        private long EstimateTokenUsage(string text)
        {
            return text.Length / 4;
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("429") ||
                       message.Contains("quota") ||
                       message.Contains("limit") ||
                       message.Contains("rate limit") ||
                       message.Contains("resource exhausted");
            }

            return base.ShouldFallback(exception);
        }

        protected virtual string GetDefaultBaseUrl()
        {
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
            return $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
        }
    }
}