namespace Assistent.Core.Tools;

public sealed class ConsoleToolApprovalService : IToolApprovalService
{
    private readonly string _providerName;
    private readonly string _providerEndpoint;

    public ConsoleToolApprovalService(string providerName = "Gemini", string providerEndpoint = "")
    {
        _providerName = providerName;
        _providerEndpoint = providerEndpoint;
    }

    public Task<bool> ApproveAsync(ToolExecutionIntent intent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var summary = string.Concat(intent.Summary.Select(character => char.IsControl(character) ? ' ' : character));
        Console.WriteLine($"A ferramenta solicita autorização: {summary}");
        if (intent.SendsLocalDataToProvider)
        {
            var destination = _providerName.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                ? $"processados pelo Ollama configurado em '{_providerEndpoint}'"
                : "enviados ao Gemini na nuvem";
            Console.WriteLine($"Os dados locais resultantes serão {destination}.");
        }

        if (!string.IsNullOrWhiteSpace(intent.RequiredConfirmationPhrase))
        {
            Console.Write($"Para confirmar, digite exatamente \"{intent.RequiredConfirmationPhrase}\": ");
            return Task.FromResult(string.Equals(
                Console.ReadLine()?.Trim(),
                intent.RequiredConfirmationPhrase,
                StringComparison.Ordinal));
        }

        Console.Write("Autorizar? [s/N]: ");
        var response = Console.ReadLine();
        var approved = string.Equals(response?.Trim(), "s", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response?.Trim(), "sim", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(approved);
    }
}
