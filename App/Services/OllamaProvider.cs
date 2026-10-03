using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Assistent.Core.Tools;

namespace Assistent.services;

public sealed class OllamaProvider : IAssistantProvider
{
    private readonly HttpClient _httpClient;
    private readonly AIOptions _options;

    public OllamaProvider(HttpClient httpClient, AIOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "Ollama";

    public async Task<AssistantProviderResponse> GenerateAsync(
        string systemPrompt,
        IReadOnlyList<AssistantMessage> messages,
        IReadOnlyList<IAssistantTool> tools,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri($"{_options.OllamaBaseUrl.TrimEnd('/')}/api/chat");
        var ollamaMessages = new List<object> { new { role = "system", content = systemPrompt } };
        ollamaMessages.AddRange(messages.Select(ToOllamaMessage));

        var declarations = tools.Select(tool => new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = ToOllamaSchema(tool.ParameterSchema)
            }
        }).ToArray();
        var payload = new
        {
            model = _options.OllamaModel,
            messages = ollamaMessages,
            tools = declarations.Length == 0 ? null : declarations,
            stream = false
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException(
                $"Não foi possível conectar ao Ollama em '{_options.OllamaBaseUrl}'. Confirme que o Ollama para Windows está em execução e que a URL está correta.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Ollama retornou HTTP {(int)response.StatusCode} ({response.StatusCode}). Verifique se o Ollama está iniciado, se o modelo '{_options.OllamaModel}' está disponível e se suporta chamadas de ferramentas.",
                    null,
                    response.StatusCode);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("A resposta do Ollama não contém o campo message esperado.");
            }

            var text = message.TryGetProperty("content", out var content)
                ? content.GetString() ?? string.Empty
                : string.Empty;
            var calls = new List<AssistantToolCall>();
            if (message.TryGetProperty("tool_calls", out var toolCalls))
            {
                if (toolCalls.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("O campo tool_calls do Ollama não é um array.");
                }

                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    if (!toolCall.TryGetProperty("function", out var function)
                        || !function.TryGetProperty("name", out var name)
                        || string.IsNullOrWhiteSpace(name.GetString()))
                    {
                        throw new InvalidDataException("O Ollama retornou uma chamada de ferramenta sem nome.");
                    }

                    var arguments = function.TryGetProperty("arguments", out var args)
                        ? args.Clone()
                        : CreateEmptyArguments();
                    calls.Add(new AssistantToolCall(name.GetString()!, arguments));
                }
            }

            return new AssistantProviderResponse(text, calls);
        }
    }

    private static object ToOllamaMessage(AssistantMessage message)
    {
        if (message.Role == "assistant" && message.ToolCalls is { Count: > 0 } calls)
        {
            return new
            {
                role = "assistant",
                content = message.Content,
                tool_calls = calls.Select(call => new
                {
                    function = new { name = call.Name, arguments = call.Arguments }
                }).ToArray()
            };
        }

        if (message.Role == "tool")
        {
            return new { role = "tool", tool_name = message.ToolName, content = message.Content };
        }

        return new { role = message.Role, content = message.Content };
    }

    private static JsonNode? ToOllamaSchema(object schema)
    {
        var node = JsonSerializer.SerializeToNode(schema);
        NormalizeSchemaTypes(node);
        return node;
    }

    private static void NormalizeSchemaTypes(JsonNode? node)
    {
        if (node is JsonObject objectNode)
        {
            foreach (var property in objectNode.ToArray())
            {
                if (property.Key.Equals("type", StringComparison.OrdinalIgnoreCase)
                    && property.Value is JsonValue typeValue
                    && typeValue.TryGetValue<string>(out var typeName))
                {
                    objectNode[property.Key] = typeName.ToLowerInvariant();
                }
                else
                {
                    NormalizeSchemaTypes(property.Value);
                }
            }
        }
        else if (node is JsonArray arrayNode)
        {
            foreach (var item in arrayNode)
            {
                NormalizeSchemaTypes(item);
            }
        }
    }

    private static JsonElement CreateEmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
