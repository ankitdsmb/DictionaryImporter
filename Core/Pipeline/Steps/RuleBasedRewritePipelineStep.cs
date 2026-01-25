// =============================================================
// FIXED FILE: DictionaryImporter.Core/Pipeline/Steps/RuleBasedRewritePipelineStep.cs
// ✅ Fixes:
//   CS0535  -> implements IImportPipelineStep.ExecuteAsync(ImportPipelineContext)
//   CS1501  -> removes extra CancellationToken argument
// =============================================================

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Pipeline.Steps;

public sealed class RuleBasedRewritePipelineStep(
    RuleBasedDefinitionEnhancementStep inner,
    ILogger<RuleBasedRewritePipelineStep> logger)
    : IImportPipelineStep
{
    public const string StepName = "RuleBasedRewrite";

    private readonly RuleBasedDefinitionEnhancementStep _inner = inner;
    private readonly ILogger<RuleBasedRewritePipelineStep> _logger = logger;

    public string Name => StepName;

    // ✅ FIX: correct interface method signature in your codebase
    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        try
        {
            // ✅ FIX: your RuleBasedDefinitionEnhancementStep.ExecuteAsync(context) has only ONE param
            await _inner.ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            // ✅ Must never crash pipeline
            _logger.LogError(ex, "RuleBasedRewritePipelineStep failed (swallowed).");
        }
    }
}