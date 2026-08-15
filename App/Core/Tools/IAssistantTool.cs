namespace Assistent.Core.Tools;

public interface IAssistantTool
{
    string Name { get; }
    string Description { get; }
    Task<object?> InvokeAsync(IDictionary<string, object?> arguments, CancellationToken cancellationToken = default);
}
