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
        catalog.LoadFromDirectory(options.PluginDirectory);

        var gemini = new GoogleGeminiProvider(new HttpClient(), options);
        _service = new AIService(gemini, catalog.BuildExecutor(), options, catalog);
    }
    public Task<string> AskAsync(string input, CancellationToken cancellationToken = default)
    {
        return _service.AskAsync(input, cancellationToken);
    }
}


