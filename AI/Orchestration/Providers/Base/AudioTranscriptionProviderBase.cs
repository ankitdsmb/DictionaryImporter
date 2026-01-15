using System;
using System.Text.Json;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Orchestration.Providers.Base
{
    public abstract class AudioTranscriptionProviderBase(
        HttpClient httpClient,
        ILogger logger,
        IOptions<ProviderConfiguration> configuration,
        IQuotaManager quotaManager = null,
        IAuditLogger auditLogger = null,
        IResponseCache responseCache = null,
        IPerformanceMetricsCollector metricsCollector = null,
        IApiKeyManager apiKeyManager = null)
        : AiProviderBase(httpClient, logger, configuration, quotaManager, auditLogger,
            responseCache, metricsCollector, apiKeyManager)
    {
        public override ProviderType Type => ProviderType.AudioTranscription;
        public override bool SupportsAudio => true;
        public override bool SupportsTranscription => true;

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.AudioTranscription = true;
            Capabilities.SupportedAudioFormats.AddRange(new[] { "mp3", "wav", "flac", "m4a" });
        }

        protected override void ValidateRequest(AiRequest request)
        {
            if (!HasAudioInput(request))
                throw new ArgumentException("Audio input required for transcription");
        }

        protected virtual bool HasAudioInput(AiRequest request)
        {
            return request.AudioData != null ||
                   !string.IsNullOrEmpty(request.Prompt) ||
                   request.AdditionalParameters?.ContainsKey("audio_url") == true;
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return CreateTranscriptionPayload(request);
        }

        protected abstract object CreateTranscriptionPayload(AiRequest request);

        protected virtual (string AudioInput, int EstimatedDuration) ProcessAudioInput(AiRequest request)
        {
            return AiProviderHelper.ProcessAudioInput(request);
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["audio_transcription"] = true;
            response.Metadata["estimated_duration_seconds"] = tokensUsed / 50;
            return response;
        }
    }
}