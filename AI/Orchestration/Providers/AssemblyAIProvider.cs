using DictionaryImporter.AI.Core.Exceptions;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public class AssemblyAiProvider : BaseCompletionProvider
    {
        private const string DefaultModel = "enhanced";
        private const int FreeTierMaxAudioMinutes = 5;
        private const int FreeTierRequestsPerMonth = 100;

        private static long _monthlyAudioSeconds = 0;
        private static long _monthlyRequestCount = 0;
        private static DateTime _lastResetMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        private static readonly object MonthlyCounterLock = new();

        public override string ProviderName => "AssemblyAI";
        public override int Priority => 8;
        public override ProviderType Type => ProviderType.AudioTranscription;

        public override bool SupportsAudio => true;

        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => true;
        public override bool IsLocal => false;

        public AssemblyAiProvider(
            HttpClient httpClient,
            ILogger<AssemblyAiProvider> logger,
            IOptions<ProviderConfiguration> configuration)
            : base(httpClient, logger, configuration)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("AssemblyAI API key not configured. Provider will be disabled.");
                return;
            }
            ConfigureAuthentication();
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.AudioTranscription = true;
            Capabilities.SupportedAudioFormats.Add("mp3");
            Capabilities.SupportedAudioFormats.Add("wav");
            Capabilities.SupportedAudioFormats.Add("flac");
            Capabilities.SupportedAudioFormats.Add("m4a");
            Capabilities.SupportedLanguages.Add("en");
            Capabilities.SupportedLanguages.Add("es");
            Capabilities.SupportedLanguages.Add("fr");
            Capabilities.SupportedLanguages.Add("de");
            Capabilities.SupportedLanguages.Add("it");
        }

        protected override sealed void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("Authorization", Configuration.ApiKey);
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
                    throw new InvalidOperationException("AssemblyAI API key not configured");
                }

                if (!CheckMonthlyRequestLimit())
                {
                    throw new AssemblyAiQuotaExceededException(
                        $"AssemblyAI free tier monthly limit reached: {FreeTierRequestsPerMonth} requests/month");
                }

                ValidateRequest(request);

                string audioInput;
                int estimatedDuration;

                if (IsAudioUrl(request.Prompt))
                {
                    audioInput = request.Prompt;
                    estimatedDuration = 60;
                }
                else if (IsAudioBase64(request.Prompt))
                {
                    audioInput = request.Prompt;
                    estimatedDuration = EstimateAudioDurationFromBase64(request.Prompt);
                }
                else
                {
                    audioInput = Convert.ToBase64String(request.AudioData);
                    estimatedDuration = EstimateAudioDurationFromBytes(request.AudioData);
                }

                if (!CheckMonthlyAudioLimit(estimatedDuration))
                {
                    throw new AssemblyAiQuotaExceededException(
                        $"AssemblyAI free tier monthly audio limit reached: {FreeTierMaxAudioMinutes} minutes/month");
                }

                IncrementMonthlyRequestCount();
                IncrementMonthlyAudioUsage(estimatedDuration);

                var transcriptId = await SubmitTranscriptionAsync(audioInput, cancellationToken);
                var result = await PollForTranscriptionAsync(transcriptId, cancellationToken);

                stopwatch.Stop();

                return new AiResponse
                {
                    Content = result.Trim(),
                    Provider = ProviderName,
                    Model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                    TokensUsed = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    Metadata = new Dictionary<string, object>
                    {
                        { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                        { "free_tier", true },
                        { "monthly_requests_used", GetMonthlyRequestCount() },
                        { "monthly_requests_remaining", FreeTierRequestsPerMonth - GetMonthlyRequestCount() },
                        { "estimated_audio_duration_seconds", estimatedDuration },
                        { "monthly_audio_seconds_used", GetMonthlyAudioSeconds() },
                        { "monthly_audio_minutes_remaining",
                            Math.Max(0, (FreeTierMaxAudioMinutes * 60) - GetMonthlyAudioSeconds()) / 60 },
                        { "transcript_id", transcriptId },
                        { "audio_format", GetAudioFormat(request) }
                    }
                };
            }
            catch (AssemblyAiQuotaExceededException ex)
            {
                stopwatch.Stop();
                Logger.LogWarning(ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "AssemblyAI provider failed");
                if (ShouldFallback(ex)) throw;

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

        private bool IsAudioUrl(string input)
        {
            return !string.IsNullOrEmpty(input) &&
                   (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    input.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private bool IsAudioBase64(string input)
        {
            return !string.IsNullOrEmpty(input) &&
                   (input.StartsWith("data:audio/", StringComparison.OrdinalIgnoreCase) ||
                    (input.Length > 100 && input.Contains("base64")));
        }

        private int EstimateAudioDurationFromBase64(string base64Data)
        {
            try
            {
                string base64String;
                if (base64Data.StartsWith("data:audio/"))
                {
                    var parts = base64Data.Split(',');
                    if (parts.Length < 2) return 60;
                    base64String = parts[1];
                }
                else
                {
                    base64String = base64Data;
                }

                var dataLength = base64String.Length * 3 / 4;
                var kilobytes = dataLength / 1024.0;

                var minutes = kilobytes / 1024.0;
                return (int)Math.Max(1, minutes * 60);
            }
            catch
            {
                return 60;
            }
        }

        private int EstimateAudioDurationFromBytes(byte[] audioData)
        {
            try
            {
                var megabytes = audioData.Length / (1024.0 * 1024.0);
                return (int)Math.Max(1, megabytes * 60);
            }
            catch
            {
                return 60;
            }
        }

        private bool CheckMonthlyRequestLimit()
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth > _lastResetMonth)
                {
                    _monthlyRequestCount = 0;
                    _monthlyAudioSeconds = 0;
                    _lastResetMonth = currentMonth;
                }
                return _monthlyRequestCount < FreeTierRequestsPerMonth;
            }
        }

        private bool CheckMonthlyAudioLimit(int additionalSeconds)
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth > _lastResetMonth)
                {
                    _monthlyRequestCount = 0;
                    _monthlyAudioSeconds = 0;
                    _lastResetMonth = currentMonth;
                }
                return (_monthlyAudioSeconds + additionalSeconds) <= (FreeTierMaxAudioMinutes * 60);
            }
        }

        private void IncrementMonthlyRequestCount()
        {
            lock (MonthlyCounterLock)
            {
                _monthlyRequestCount++;
            }
        }

        private void IncrementMonthlyAudioUsage(int seconds)
        {
            lock (MonthlyCounterLock)
            {
                _monthlyAudioSeconds += seconds;
            }
        }

        private long GetMonthlyRequestCount()
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth > _lastResetMonth)
                {
                    _monthlyRequestCount = 0;
                    _monthlyAudioSeconds = 0;
                    _lastResetMonth = currentMonth;
                }
                return _monthlyRequestCount;
            }
        }

        private long GetMonthlyAudioSeconds()
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth <= _lastResetMonth) return _monthlyAudioSeconds;
                _monthlyRequestCount = 0;
                _monthlyAudioSeconds = 0;
                _lastResetMonth = currentMonth;
                return _monthlyAudioSeconds;
            }
        }

        private void ValidateRequest(AiRequest request)
        {
            var hasAudioUrl = IsAudioUrl(request.Prompt);
            var hasAudioBase64 = IsAudioBase64(request.Prompt);
            var hasAudioData = request.AudioData.Length > 0;

            if (!hasAudioUrl && !hasAudioBase64 && !hasAudioData)
            {
                throw new ArgumentException("Audio URL, base64 data, or AudioData required for AssemblyAI transcription");
            }
        }

        private async Task<string> SubmitTranscriptionAsync(
            string audioInput,
            CancellationToken cancellationToken)
        {
            var payload = CreateTranscriptionPayload(audioInput);
            var httpRequest = CreateHttpRequest(payload);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            using var jsonDoc = JsonDocument.Parse(content);
            if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
            {
                return idElement.GetString() ?? throw new InvalidOperationException("No transcript ID received");
            }

            if (!jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
                throw new InvalidOperationException("Invalid AssemblyAI response format");
            var errorMessage = errorElement.GetString() ?? "Unknown error";
            throw new HttpRequestException($"AssemblyAI submission error: {errorMessage}");
        }

        private async Task<string> PollForTranscriptionAsync(
            string transcriptId,
            CancellationToken cancellationToken)
        {
            var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
                "https://api.assemblyai.com/v2/transcript" : Configuration.BaseUrl;

            var pollUrl = $"{baseUrl}/{transcriptId}";
            const int maxAttempts = 30;
            var attempt = 0;

            while (attempt < maxAttempts)
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, pollUrl);

                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                using var jsonDoc = JsonDocument.Parse(content);
                var status = jsonDoc.RootElement.GetProperty("status").GetString();

                if (status != "completed")
                {
                    if (status == "error")
                    {
                        var error = jsonDoc.RootElement.GetProperty("error").GetString() ?? "Unknown error";
                        throw new HttpRequestException($"AssemblyAI transcription error: {error}");
                    }
                }
                else
                {
                    return jsonDoc.RootElement.GetProperty("text").GetString() ?? string.Empty;
                }

                await Task.Delay(2000, cancellationToken);
                attempt++;
            }

            throw new TimeoutException("AssemblyAI transcription timeout after 60 seconds");
        }

        private object CreateTranscriptionPayload(string audioInput)
        {
            var payload = new Dictionary<string, object>();

            if (IsAudioUrl(audioInput))
            {
                payload["audio_url"] = audioInput;
            }
            else if (IsAudioBase64(audioInput))
            {
                payload["audio_data"] = audioInput;
            }
            else
            {
                payload["audio_data"] = $"data:audio/mp3;base64,{audioInput}";
            }

            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            if (model == "enhanced" || model.Contains("enhanced"))
            {
                payload["language_detection"] = true;
                payload["speech_model"] = "enhanced";
            }
            else
            {
                payload["language_code"] = "en_us";
            }

            payload["punctuate"] = true;
            payload["format_text"] = true;
            payload["auto_highlights"] = false;
            payload["auto_chapters"] = false;
            payload["entity_detection"] = false;

            foreach (var setting in Configuration.AdditionalSettings.Where(setting => !payload.ContainsKey(setting.Key)))
            {
                payload[setting.Key] = setting.Value;
            }

            return payload;
        }

        private HttpRequestMessage CreateHttpRequest(object payload)
        {
            var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
                "https://api.assemblyai.com/v2/transcript" : Configuration.BaseUrl;

            return new HttpRequestMessage(HttpMethod.Post, baseUrl)
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

        private string GetAudioFormat(AiRequest request)
        {
            if (!string.IsNullOrEmpty(request.AudioFormat))
                return request.AudioFormat;

            if (IsAudioUrl(request.Prompt))
            {
                var url = request.Prompt.ToLower();
                if (url.Contains(".mp3")) return "mp3";
                if (url.Contains(".wav")) return "wav";
                if (url.Contains(".flac")) return "flac";
                if (url.Contains(".m4a")) return "m4a";
            }

            if (IsAudioBase64(request.Prompt))
            {
                if (request.Prompt.Contains("data:audio/mp3")) return "mp3";
                if (request.Prompt.Contains("data:audio/wav")) return "wav";
                if (request.Prompt.Contains("data:audio/flac")) return "flac";
                if (request.Prompt.Contains("data:audio/m4a")) return "m4a";
            }

            return "unknown";
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is AssemblyAiQuotaExceededException)
                return true;

            if (exception is not HttpRequestException httpEx) return base.ShouldFallback(exception);
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("monthly") ||
                   message.Contains("free tier");
        }
    }
}