namespace Assistent.Core.Tools;

public sealed record ToolExecutionResult(string ToolName, string Result, bool Success = true);
