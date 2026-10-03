using Assistent.services;
using Assistent.Core.Tools;

namespace Assistent;

class Myelin
{
    private readonly AIService _service;

    public Myelin()
    {
        var options = AIOptions.Load();

        var catalog = new ToolCatalog();
        catalog.DiscoverToolsFromCurrentAssembly();
        catalog.LoadFromDirectory(options.PluginDirectory, options.AllowedPlugins);

        var httpClient = new HttpClient();
        IAssistantProvider provider = options.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
            ? new OllamaProvider(httpClient, options)
            : new GoogleGeminiProvider(httpClient, options);
        _service = new AIService(
            provider,
            catalog.BuildExecutor(new ConsoleToolApprovalService(options.Provider, options.OllamaBaseUrl)),
            options,
            new Assistent.Core.Guardrails.GuardrailPipeline(
                options.Guardrails,
                new[] { options.GoogleApiKey, Environment.GetEnvironmentVariable("BRAVE_API_KEY") },
                reason => Console.WriteLine($"[guardrail] {reason}")));
        Console.WriteLine($"Provedor de IA: {provider.Name}.");
    }
    public Task<string> AskAsync(string input, CancellationToken cancellationToken = default)
    {
        return _service.AskAsync(input, cancellationToken);
    }
}
