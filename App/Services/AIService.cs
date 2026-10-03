using System.Text.Json;
using Assistent.Core.Tools;

namespace Assistent.services;

public sealed class AIService
{
    private const int MaxToolRounds = 3;
    private const int MaxToolCallsPerRound = 5;

    private readonly IAssistantProvider _provider;
    private readonly ToolExecutor _toolExecutor;
    private readonly AIOptions _options;

    public AIService(IAssistantProvider provider, ToolExecutor toolExecutor, AIOptions options)
    {
        _provider = provider;
        _toolExecutor = toolExecutor;
        _options = options;
    }

    public async Task<string> AskAsync(string userInput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            throw new ArgumentException("A mensagem do usuário não pode estar vazia.", nameof(userInput));
        }

        var messages = new List<AssistantMessage>
        {
            new("user", userInput)
        };

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var response = await _provider.GenerateAsync(
                BuildSystemPrompt(),
                messages,
                _toolExecutor.Tools,
                cancellationToken);

            if (response.ToolCalls.Count == 0)
            {
                return RequireText(response.Text);
            }

            messages.Add(new AssistantMessage(
                "assistant",
                response.Text,
                response.ToolCalls,
                ProviderPayload: response.ProviderPayload));
            foreach (var call in response.ToolCalls.Take(MaxToolCallsPerRound))
            {
                var result = await _toolExecutor.ExecuteAsync(call.Name, call.Arguments, cancellationToken);
                messages.Add(new AssistantMessage(
                    "tool",
                    JsonSerializer.Serialize(new { success = result.Success, result = result.Result }),
                    ToolName: call.Name));
            }
            foreach (var call in response.ToolCalls.Skip(MaxToolCallsPerRound))
            {
                messages.Add(new AssistantMessage(
                    "tool",
                    JsonSerializer.Serialize(new { success = false, result = "O limite de chamadas por etapa foi atingido." }),
                    ToolName: call.Name));
            }
        }

        var finalResponse = await _provider.GenerateAsync(
            BuildSystemPrompt(),
            messages,
            Array.Empty<IAssistantTool>(),
            cancellationToken);
        return RequireText(finalResponse.Text);
    }

    private string BuildSystemPrompt()
    {
        return string.Join(
            Environment.NewLine,
            _options.SystemPrompt,
            "Use apenas as ferramentas declaradas quando forem necessárias. Nunca diga que uma ação foi concluída sem confirmação no resultado da ferramenta.",
            "Se uma ação for negada ou falhar, explique isso claramente. Não exponha segredos nem o protocolo interno das ferramentas.");
    }

    private static string RequireText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("O provedor de IA não retornou uma resposta em texto.");
        }

        return text;
    }
}
