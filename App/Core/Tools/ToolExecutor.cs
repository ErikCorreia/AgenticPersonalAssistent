using System.Text.Json;

namespace Assistent.Core.Tools;

public sealed class ToolExecutor
{
    private readonly IReadOnlyList<IAssistantTool> _tools;
    private readonly IToolApprovalService _approvalService;

    public ToolExecutor(IEnumerable<IAssistantTool> tools, IToolApprovalService approvalService)
    {
        _tools = tools.ToList();
        _approvalService = approvalService;
    }

    public IReadOnlyList<IAssistantTool> Tools => _tools;

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        JsonElement jsonArguments,
        CancellationToken cancellationToken = default)
    {
        if (jsonArguments.ValueKind != JsonValueKind.Object)
        {
            return new ToolExecutionResult(toolName, "Os argumentos da ferramenta devem ser um objeto JSON.", false);
        }

        var tool = _tools.FirstOrDefault(candidate =>
            candidate.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return new ToolExecutionResult(toolName, "A ferramenta solicitada não está disponível.", false);
        }

        try
        {
            var arguments = jsonArguments.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ConvertJsonValue(property.Value),
                StringComparer.OrdinalIgnoreCase);
            var intent = tool.GetExecutionIntent(arguments);
            if ((intent.RequiresApproval || intent.SendsLocalDataToProvider)
                && !await _approvalService.ApproveAsync(intent, cancellationToken))
            {
                return new ToolExecutionResult(tool.Name, "Ação não autorizada pelo usuário.", false);
            }

            var value = await tool.InvokeAsync(arguments, cancellationToken);
            if (value is null)
            {
                return new ToolExecutionResult(tool.Name, "A ferramenta retornou um resultado vazio.", false);
            }

            var result = value is string text ? text : JsonSerializer.Serialize(value);
            var success = TryReadSuccess(result);
            return new ToolExecutionResult(tool.Name, result, success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ToolExecutionResult(tool.Name, $"A ferramenta falhou: {exception.Message}", false);
        }
        catch (PlatformNotSupportedException exception)
        {
            return new ToolExecutionResult(tool.Name, $"A ferramenta não está disponível nesta plataforma: {exception.Message}", false);
        }
    }

    private static bool TryReadSuccess(string result)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Success", out var success)
                || success.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static object? ConvertJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToArray(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ConvertJsonValue(property.Value),
            StringComparer.OrdinalIgnoreCase),
        _ => throw new ArgumentException("Os argumentos da ferramenta contêm um valor JSON inválido.")
    };
}
