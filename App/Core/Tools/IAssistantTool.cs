namespace Assistent.Core.Tools;

public interface IAssistantTool
{
    string Name { get; }
    string Description { get; }
    object ParameterSchema => new
    {
        type = "OBJECT",
        properties = new Dictionary<string, object>()
    };
    ToolExecutionIntent GetExecutionIntent(IDictionary<string, object?> arguments) =>
        ToolExecutionIntent.Untrusted(Name);
    Task<object?> InvokeAsync(IDictionary<string, object?> arguments, CancellationToken cancellationToken = default);
}
