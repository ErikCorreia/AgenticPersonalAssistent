using Assistent.services;
using Assistent.Core.Tools;

namespace Assistent.services;

public sealed class AIService
{
    private readonly GoogleGeminiProvider _gemini;
    private readonly ToolExecutor _toolExecutor;
    private readonly AIOptions _options;
    private readonly ToolCatalog _catalog;

    public AIService(GoogleGeminiProvider gemini, ToolExecutor toolExecutor, AIOptions options, ToolCatalog catalog)
    {
        _gemini = gemini;
        _toolExecutor = toolExecutor;
        _options = options;
        _catalog = catalog;
    }

    public async Task<string> AskAsync(string userInput, CancellationToken cancellationToken = default)
    {
        _ = _catalog.CreateKernel();

        var toolDescriptions = string.Join(
            Environment.NewLine,
            _toolExecutor.Tools.Select(tool => $"- {tool.Name}: {tool.Description}"));

        var history = new List<string>();
        var toolResults = new List<ToolExecutionResult>();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var prompt = BuildIterationPrompt(userInput, toolDescriptions, history, toolResults);
            var modelResponse = await _gemini.GenerateAsync(prompt, cancellationToken);
            history.Add(modelResponse);

            var executed = await _toolExecutor.ExecuteAsync(modelResponse, cancellationToken);
            if (executed.Count == 0)
            {
                return modelResponse;
            }

            toolResults.AddRange(executed);

            if (ShouldStopAfterToolExecution(modelResponse, executed))
            {
                break;
            }
        }

        return await BuildFinalResponseAsync(userInput, history, toolResults, toolDescriptions, cancellationToken);
    }

    private async Task<string> BuildFinalResponseAsync(string userInput, IReadOnlyList<string> history, IReadOnlyList<ToolExecutionResult> toolResults, string toolDescriptions, CancellationToken cancellationToken)
    {
        var prompt = string.Join(
            Environment.NewLine,
            new[]
            {
                "You are Myelin, a personal assistant.",
                "You have already executed tools and gathered results. Now produce the final answer for the user.",
                "Do not output raw tool calls or JSON. Respond in natural language only.",
                string.Empty,
                "Available tools:",
                toolDescriptions,
                string.Empty,
                "Conversation history:",
                string.Join(Environment.NewLine, history),
                string.Empty,
                "Tool results:",
                string.Join(Environment.NewLine, toolResults.Select(result => $"- {result.ToolName}: {result.Result}")),
                string.Empty,
                "User request:",
                userInput
            });

        return await _gemini.GenerateAsync(prompt, cancellationToken);
    }

    private static bool ShouldStopAfterToolExecution(string modelResponse, IReadOnlyList<ToolExecutionResult> executed)
    {
        return executed.Count > 0 && !modelResponse.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildIterationPrompt(string userInput, string toolDescriptions, IReadOnlyList<string> history, IReadOnlyList<ToolExecutionResult> toolResults)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                "You are Myelin, a personal assistant.",
                "Use tools when it helps answer the user's request accurately.",
                "If you need information from the web, call a web tool.",
                "If you already have tool results, use them to answer the user.",
                string.Empty,
                "Available tools:",
                toolDescriptions,
                string.Empty,
                "Conversation history:",
                string.Join(Environment.NewLine, history),
                string.Empty,
                "Tool results so far:",
                toolResults.Count == 0 ? "None" : string.Join(Environment.NewLine, toolResults.Select(result => $"- {result.ToolName}: {result.Result}")),
                string.Empty,
                "If you need more data, return a tool call like this:",
                "<tool_call>{\"tool\":\"web_search\",\"arguments\":{\"query\":\"news about technology\"}}</tool_call>",
                string.Empty,
                "If you have enough information, answer the user directly in natural language without showing the tool call.",
                string.Empty,
                "User request:",
                userInput
            });
    }

    private static string BuildInitialPrompt(string userInput, string toolDescriptions)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                "You are Myelin, a personal assistant.",
                "Use tools when they help answer the user accurately.",
                string.Empty,
                "Available tools:",
                toolDescriptions,
                string.Empty,
                "You may invoke one or more tools in sequence when useful.",
                "If you need to execute tools, return them in this exact format:",
                "<tool_call>{\"tool\":\"web_search\",\"arguments\":{\"query\":\"news about technology\"}}</tool_call>",
                string.Empty,
                "After running tools, synthesize the results into a concise natural-language answer with references when relevant.",
                string.Empty,
                "User request:",
                userInput
            });
    }

    private static string BuildFollowUpPrompt(string userInput, string previousResponse, IReadOnlyList<ToolExecutionResult> toolResults, string toolDescriptions)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                "You are Myelin, a personal assistant.",
                "The previous step used tools. Review the tool results and decide whether more tool calls are needed.",
                "If more information is necessary, issue another tool call. Otherwise, answer the user in natural language directly, without repeating the raw tool call block.",
                string.Empty,
                "Available tools:",
                toolDescriptions,
                string.Empty,
                "Previous model response:",
                previousResponse,
                string.Empty,
                "Tool results:",
                string.Join(Environment.NewLine, toolResults.Select(result => $"- {result.ToolName}: {result.Result}")),
                string.Empty,
                "User request:",
                userInput
            });
    }
}
