namespace Planvexa.Api.Ai;

using Planvexa.Modules.Ai.Domain;
using Planvexa.SharedContracts.Ai;

/// <summary>
/// The default AI completion provider: a deterministic, offline, extractive implementation (no external
/// calls, no API keys) so AI assistance is fully testable and works out of the box. It delegates to the
/// pure <see cref="ExtractiveAi"/> logic. A real LLM provider (OpenAI/Azure/Bedrock) is a drop-in
/// replacement for this registration — the Ai module depends only on <see cref="IAiCompletionProvider"/>.
/// </summary>
public sealed class DeterministicAiCompletionProvider : IAiCompletionProvider
{
    public Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default)
        => Task.FromResult(ExtractiveAi.Complete(prompt));
}
