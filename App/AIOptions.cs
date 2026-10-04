using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Assistent;

public sealed class AIOptions
{
    public string GoogleApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gemini-2.0-flash";
    public string Provider { get; init; } = "Gemini";
    public string OllamaBaseUrl { get; init; } = "http://localhost:11434";
    public string OllamaModel { get; init; } = "qwen3:8b";
    public string PluginDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "plugins");
    public string SystemPrompt { get; init; } = "Você é um assistente pessoal. Responda em português e use ferramentas quando apropriado.";
    public Assistent.Core.Guardrails.GuardrailOptions Guardrails { get; init; } = new();
    public Assistent.Speech.SpeechOptions Speech { get; init; } = new();
    public string[] AllowedPlugins { get; init; } = new[] { "ExampleToolkit.dll", "FileManagerToolkit.dll", "WebSearchToolkit.dll" };

    public static AIOptions Load()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var configurationPath = Path.Combine(baseDirectory, "appsettings.json");
        var configurationExists = File.Exists(configurationPath);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var googleAiSection = configuration.GetSection("GoogleAi");
        var assistentSection = configuration.GetSection("Assistent");
        var provider = Environment.GetEnvironmentVariable("ASSISTENT_PROVIDER")
            ?? assistentSection["Provider"]
            ?? "Gemini";
        if (!provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
            && !provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Provedor '{provider}' não suportado. Use Gemini ou Ollama.");
        }

        var apiKey = Environment.GetEnvironmentVariable("GOOGLE_AI_API_KEY") ?? string.Empty;
        var model = googleAiSection["Model"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_AI_MODEL")
            ?? string.Empty;
        var configurationNeedsUpdate = !configurationExists
            || !string.IsNullOrWhiteSpace(googleAiSection["ApiKey"]);

        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = PromptSecret("Informe a Google Gemini API key (não será salva): ");
            configurationNeedsUpdate = true;
        }

        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
            && (!configurationExists || string.IsNullOrWhiteSpace(model)))
        {
            model = PromptWithDefault("Informe o modelo Gemini", "gemini-2.0-flash");
            configurationNeedsUpdate = true;
        }

        var pluginDirectory = assistentSection["PluginDirectory"]
            ?? Environment.GetEnvironmentVariable("PLUGIN_DIRECTORY")
            ?? "plugins";
        var systemPromptSetting = assistentSection["SystemPrompt"]
            ?? "You are a personal assistant. Answer in Portuguese and use tools when appropriate.";
        var configuredPlugins = Environment.GetEnvironmentVariable("ASSISTENT_ALLOWED_PLUGINS");
        var allowedPlugins = string.IsNullOrWhiteSpace(configuredPlugins)
            ? assistentSection.GetSection("AllowedPlugins").GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
            : configuredPlugins.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var systemPrompt = LoadSystemPrompt(systemPromptSetting, baseDirectory);

        var options = new AIOptions
        {
            GoogleApiKey = apiKey,
            Model = model,
            Provider = provider,
            OllamaBaseUrl = NormalizeBaseUrl(
                Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
                    ?? assistentSection["OllamaBaseUrl"]
                    ?? "http://localhost:11434"),
            OllamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
                ?? assistentSection["OllamaModel"]
                ?? "qwen3:8b",
            Speech = LoadSpeech(configuration.GetSection("Speech")),
            PluginDirectory = ResolvePath(pluginDirectory, baseDirectory),
            SystemPrompt = systemPrompt,
            AllowedPlugins = allowedPlugins.Length == 0
                ? new[] { "ExampleToolkit.dll", "FileManagerToolkit.dll", "WebSearchToolkit.dll" }
                : allowedPlugins
        };

        if (configurationNeedsUpdate)
        {
            Save(configurationPath, options);
            Console.WriteLine($"Configuração criada em: {configurationPath}");
        }

        return options;
    }

    private static string Prompt(string message)
    {
        Console.Write(message);
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    private static string PromptSecret(string message)
    {
        Console.Write(message);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine()?.Trim() ?? string.Empty;
        }

        var secret = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return secret.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && secret.Length > 0)
            {
                secret.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }
    }

    private static string PromptWithDefault(string message, string defaultValue)
    {
        var value = Prompt($"{message} [{defaultValue}]: ");
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static string LoadSystemPrompt(string setting, string baseDirectory)
    {
        var promptPath = ResolvePath(setting, baseDirectory);
        return File.Exists(promptPath) && Path.GetExtension(promptPath).Equals(".md", StringComparison.OrdinalIgnoreCase)
            ? File.ReadAllText(promptPath)
            : setting;
    }

    private static Assistent.Speech.SpeechOptions LoadSpeech(IConfigurationSection s)
    {
        var d = new Assistent.Speech.SpeechOptions();
        return new Assistent.Speech.SpeechOptions
        {
            Language = s["Language"] ?? d.Language,
            WhisperModelPath = s["WhisperModelPath"] ?? d.WhisperModelPath,
            Voice = s["Voice"] ?? d.Voice,
            TtsProvider = s["TtsProvider"] ?? d.TtsProvider,
            ElevenLabsVoiceId = s["ElevenLabsVoiceId"] ?? d.ElevenLabsVoiceId,
            ElevenLabsModel = s["ElevenLabsModel"] ?? d.ElevenLabsModel,
            PiperPath = s["PiperPath"] ?? d.PiperPath,
            PiperModelPath = s["PiperModelPath"] ?? d.PiperModelPath,
            Rate = int.TryParse(s["Rate"], out var r) ? r : d.Rate,
            SpeakResponses = bool.TryParse(s["SpeakResponses"], out var sp) ? sp : d.SpeakResponses,
            MaxListenSeconds = int.TryParse(s["MaxListenSeconds"], out var m) ? m : d.MaxListenSeconds,
            SilenceMilliseconds = int.TryParse(s["SilenceMilliseconds"], out var si) ? si : d.SilenceMilliseconds
        };
    }

    private static void Save(string path, AIOptions options)
    {
        var document = new
        {
            GoogleAi = new { Model = options.Model },
            Assistent = new
            {
                Provider = options.Provider,
                OllamaBaseUrl = options.OllamaBaseUrl,
                OllamaModel = options.OllamaModel,
                PluginDirectory = "plugins",
                AllowedPlugins = options.AllowedPlugins,
                SystemPrompt = "Prompts/system-prompt.md"
            }
        };

        File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("OllamaBaseUrl deve ser uma URL HTTP ou HTTPS absoluta.");
        }

        return uri.ToString().TrimEnd('/');
    }
}
