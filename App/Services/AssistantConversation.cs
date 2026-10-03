using System.Text.Json;

namespace Assistent.services;

public sealed record AssistantToolCall(string Name, JsonElement Arguments);

public sealed record AssistantMessage(
    string Role,
    string Content,
    IReadOnlyList<AssistantToolCall>? ToolCalls = null,
    string? ToolName = null,
    JsonElement? ProviderPayload = null);

public sealed record AssistantProviderResponse(
    string Text,
    IReadOnlyList<AssistantToolCall> ToolCalls,
    JsonElement? ProviderPayload = null);
