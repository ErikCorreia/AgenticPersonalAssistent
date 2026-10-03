namespace Assistent.Core.Tools;

public sealed record ToolExecutionIntent(
    bool RequiresApproval,
    bool SendsLocalDataToProvider,
    string Summary,
    string? RequiredConfirmationPhrase = null)
{
    public static ToolExecutionIntent Safe { get; } = new(false, false, string.Empty);

    public static ToolExecutionIntent Untrusted(string toolName) =>
        new(true, true, $"Executar o plugin não classificado '{toolName}'.");
}
