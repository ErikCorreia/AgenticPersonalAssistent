using Assistent.Core.Tools;

namespace Assistent.services;

public interface IAssistantProvider
{
    string Name { get; }

    Task<AssistantProviderResponse> GenerateAsync(
        string systemPrompt,
        IReadOnlyList<AssistantMessage> messages,
        IReadOnlyList<IAssistantTool> tools,
        CancellationToken cancellationToken = default);
}
