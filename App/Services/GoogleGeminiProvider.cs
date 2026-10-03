using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Assistent.Core.Tools;

namespace Assistent.services;

public sealed class GoogleGeminiProvider : IAssistantProvider
{
    private readonly HttpClient _httpClient;
    private readonly AIOptions _options;

    public GoogleGeminiProvider(HttpClient httpClient, AIOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "Gemini";

    public async Task<AssistantProviderResponse> GenerateAsync(
        string systemPrompt,
        IReadOnlyList<AssistantMessage> messages,
        IReadOnlyList<IAssistantTool> tools,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.GoogleApiKey))
        {
            throw new InvalidOperationException("A variável de ambiente GOOGLE_AI_API_KEY não está configurada.");
        }

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent";
        var declarations = tools.Select(tool => new
        {
            name = tool.Name,
            description = tool.Description,
            parameters = ToGeminiSchema(tool.ParameterSchema)
        }).ToArray();
        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = BuildContents(messages),
            tools = declarations.Length == 0
                ? null
                : new[] { new { functionDeclarations = declarations } }
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("x-goog-api-key", _options.GoogleApiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return await ParseResponseAsync(response, cancellationToken);
                }

                var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (transient && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                    continue;
                }

                throw new HttpRequestException(
                    $"A API Gemini retornou HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null,
                    response.StatusCode);
            }
        }

        throw new HttpRequestException("O serviço Gemini permanece indisponível após três tentativas.");
    }

    private static IReadOnlyList<object> BuildContents(IReadOnlyList<AssistantMessage> messages)
    {
        var contents = new List<object>();
        var index = 0;
        while (index < messages.Count)
        {
            var message = messages[index];
            if (message.Role == "tool")
            {
                var responseParts = new List<object>();
                while (index < messages.Count && messages[index].Role == "tool")
                {
                    var toolMessage = messages[index++];
                    responseParts.Add(new
                    {
                        functionResponse = new
                        {
                            name = toolMessage.ToolName,
                            response = new { result = toolMessage.Content }
                        }
                    });
                }

                contents.Add(new { role = "user", parts = responseParts });
                continue;
            }

            if (message.Role == "assistant" && message.ProviderPayload is { } providerPayload)
            {
                contents.Add(providerPayload);
                index++;
                continue;
            }

            var role = message.Role == "assistant" ? "model" : "user";
            contents.Add(new
            {
                role,
                parts = new[] { new { text = message.Content } }
            });
            index++;
        }

        return contents;
    }

    private static JsonNode? ToGeminiSchema(object schema)
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
                    objectNode[property.Key] = typeName.ToUpperInvariant();
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

    private static async Task<AssistantProviderResponse> ParseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            throw new InvalidDataException("A resposta Gemini não contém uma mensagem candidata válida.");
        }

        var text = new List<string>();
        var calls = new List<AssistantToolCall>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textPart))
            {
                text.Add(textPart.GetString() ?? string.Empty);
            }

            if (part.TryGetProperty("functionCall", out var functionCall))
            {
                if (!functionCall.TryGetProperty("name", out var nameElement)
                    || string.IsNullOrWhiteSpace(nameElement.GetString()))
                {
                    throw new InvalidDataException("A resposta Gemini contém uma chamada de ferramenta sem nome.");
                }

                var arguments = functionCall.TryGetProperty("args", out var args)
                    ? args.Clone()
                    : CreateEmptyArguments();
                calls.Add(new AssistantToolCall(nameElement.GetString()!, arguments));
            }
        }

        return new AssistantProviderResponse(
            string.Join(Environment.NewLine, text),
            calls,
            content.Clone());
    }

    private static JsonElement CreateEmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
