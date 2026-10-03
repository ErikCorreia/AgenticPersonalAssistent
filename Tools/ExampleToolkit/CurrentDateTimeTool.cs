using Assistent.Core.Tools;

namespace ExampleToolkit;

public sealed class CurrentDateTimeTool : IAssistantTool
{
    public string Name => "current_datetime";

    public string Description => "Retorna a data e hora local atuais do computador.";

    public ToolExecutionIntent GetExecutionIntent(IDictionary<string, object?> arguments) =>
        ToolExecutionIntent.Safe;

    public Task<object?> InvokeAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var currentDateTime = DateTimeOffset.Now;
        var result = new
        {
            Local = currentDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            Utc = currentDateTime.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
        };

        return Task.FromResult<object?>(result);
    }
}
