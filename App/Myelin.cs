using Assistent.services;
using Assistent.Core.Tools;
using Assistent.Speech;

namespace Assistent;

class Myelin
{
    private readonly AIService _service;
    private readonly AIOptions _options;
    private readonly Assistent.Core.Guardrails.GuardrailPipeline _guardrails;
    private ISpeechToText? _stt;
    private ITextToSpeech? _tts;
    public bool SpeakResponses { get; set; }

    public Myelin()
    {
        var options = AIOptions.Load();
        _options = options;
        SpeakResponses = options.Speech.SpeakResponses;
        _guardrails = new Assistent.Core.Guardrails.GuardrailPipeline(options.Guardrails, new[] { options.GoogleApiKey, Environment.GetEnvironmentVariable("BRAVE_API_KEY") }, reason => Console.WriteLine($"[guardrail] {reason}"));

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
            _guardrails);
        Console.WriteLine($"Provedor de IA: {provider.Name}.");
    }
    public Task<string> AskAsync(string input, CancellationToken cancellationToken = default)
    {
        return _service.AskAsync(input, cancellationToken);
    }

    public async Task<string?> ListenAsync(CancellationToken cancellationToken = default)
    {
        _stt ??= new WhisperLocalStt(_options.Speech);
        var transcript = await _stt.ListenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(transcript)) return null;
        var check = _guardrails.CheckUserInput(transcript);
        return check.IsBlocked ? null : check.Text;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        var clean = SpeechTextCleaner.Clean(text);
        if (clean.Length == 0) return;
        _tts ??= _options.Speech.TtsProvider.ToLowerInvariant() switch
        {
            "piper" => new PiperTts(_options.Speech),
            "elevenlabs" => new ElevenLabsTts(_options.Speech),
            _ => new WindowsTts(_options.Speech)
        };
        await _tts.SpeakAsync(clean, cancellationToken);
    }
}