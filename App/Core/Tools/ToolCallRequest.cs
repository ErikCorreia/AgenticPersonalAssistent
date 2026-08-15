using System.Text.Json;

namespace Assistent.Core.Tools;

public sealed class ToolCallRequest
{
    public string? Tool { get; set; }
    public Dictionary<string, JsonElement>? Arguments { get; set; }
}
