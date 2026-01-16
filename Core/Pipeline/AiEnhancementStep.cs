namespace DictionaryImporter.Core.Pipeline
{
    public sealed class AiEnhancementStep
    {
        private readonly ICompletionOrchestrator _orchestrator;
        private readonly IAiAnnotationRepository _repo;
        private readonly ILogger<AiEnhancementStep> _logger;
        private readonly bool _enabled;

        public AiEnhancementStep(
            ICompletionOrchestrator orchestrator,
            IAiAnnotationRepository repo,
            IConfiguration configuration,
            ILogger<AiEnhancementStep> logger)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _enabled = configuration.GetValue<bool>("AI:EnablePipelineSteps:AiEnhancement", defaultValue: false);
        }

        public async Task ExecuteAsync(string sourceCode, CancellationToken ct)
        {
            if (!_enabled)
            {
                _logger.LogInformation("Stage=AiEnhancement skipped (disabled) | Code={Code}", sourceCode);
                return;
            }

            _logger.LogInformation("Stage=AiEnhancement started | Code={Code}", sourceCode);

            // STEP 1: fetch candidates
            var candidates = await _repo.GetDefinitionCandidatesAsync(sourceCode, take: 50, ct);
            if (candidates.Count == 0)
            {
                _logger.LogInformation("Stage=AiEnhancement no candidates | Code={Code}", sourceCode);
                return;
            }

            // STEP 2: Here you call orchestrator using YOUR existing AI contracts.
            // IMPORTANT:
            // I am not creating AITask because it is not found in your solution.
            // So, keep AI orchestration call inside your AI module service,
            // or implement it using the actual request type you already have.

            // For now: safe behavior (no crash)
            _logger.LogWarning(
                "Stage=AiEnhancement orchestrator call not executed because AITask type is missing. " +
                "Please map to your real AI request DTO inside DictionaryImporter.AI.");

            _logger.LogInformation("Stage=AiEnhancement completed | Code={Code}", sourceCode);
        }
    }
}