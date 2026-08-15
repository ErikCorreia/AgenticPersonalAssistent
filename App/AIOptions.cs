using System.IO;
using Microsoft.Extensions.Configuration;

namespace Assistent;

public sealed class AIOptions
{
    public string GoogleApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gemini-2.0-flash";
    public string PluginDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "plugins");
    public string SystemPrompt { get; init; } = "You a personal assistant. Answer in Portuguese and use tools when appropriate.";

    public static AIOptions Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var googleAiSection = configuration.GetSection("GoogleAi");
        var assistentSection = configuration.GetSection("Assistent");

        return new AIOptions
        {
            GoogleApiKey = googleAiSection["ApiKey"]
                ?? Environment.GetEnvironmentVariable("GOOGLE_AI_API_KEY")
                ?? string.Empty,
            Model = googleAiSection["Model"]
                ?? Environment.GetEnvironmentVariable("GOOGLE_AI_MODEL")
                ?? "gemini-2.0-flash",
            PluginDirectory = assistentSection["PluginDirectory"]
                ?? Environment.GetEnvironmentVariable("PLUGIN_DIRECTORY")
                ?? Path.Combine(AppContext.BaseDirectory, "plugins"),
            SystemPrompt = assistentSection["SystemPrompt"]
                ?? "You are a personal assistant. Answer in Portuguese and use tools when appropriate."
        };
    }
}
