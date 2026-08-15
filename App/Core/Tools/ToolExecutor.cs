using System.Text.Json;
using System.Text.RegularExpressions;

namespace Assistent.Core.Tools;

public sealed class ToolExecutor
{
    private readonly IReadOnlyList<IAssistantTool> _tools;

    public ToolExecutor(IEnumerable<IAssistantTool> tools)
    {
        _tools = tools.ToList();
    }

    public IReadOnlyList<IAssistantTool> Tools => _tools;

    public async Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(string response, CancellationToken cancellationToken = default)
    {
        var matches = Regex.Matches(response, "<tool_call>(?<json>.*?)</tool_call>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var results = new List<ToolExecutionResult>();

        foreach (Match match in matches)
        {
            var request = JsonSerializer.Deserialize<ToolCallRequest>(match.Groups["json"].Value, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request?.Tool is null)
            {
                continue;
            }

            var tool = _tools.FirstOrDefault(t => t.Name.Equals(request.Tool, StringComparison.OrdinalIgnoreCase));
            if (tool is null)
            {
                results.Add(new ToolExecutionResult(request.Tool, $"Tool '{request.Tool}' was not found."));
                continue;
            }

            var arguments = request.Arguments?.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertJsonValue(kvp.Value)) ?? new Dictionary<string, object?>();

            var result = await tool.InvokeAsync(arguments, cancellationToken);
            results.Add(new ToolExecutionResult(tool.Name, result?.ToString() ?? "The tool returned no result."));
        }

        return results;
    }

    private static object? ConvertJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };
}
