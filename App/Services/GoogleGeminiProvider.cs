using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Assistent.services;

public sealed class GoogleGeminiProvider
{
    private readonly HttpClient _httpClient;
    private readonly AIOptions _options;

    public GoogleGeminiProvider(HttpClient httpClient, AIOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.GoogleApiKey))
        {
            return "No GOOGLE_AI_API_KEY was configured. The assistant can still use local tools, but Gemini is unavailable.";
        }

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent?key={_options.GoogleApiKey}";
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    return json.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString() ?? string.Empty;
                }

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt == 3)
                    {
                        return "The Gemini service is temporarily unavailable because of rate limiting. Please try again in a moment.";
                    }

                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                    continue;
                }

                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return $"The Gemini API returned {(int)response.StatusCode}. Please try again later. Details: {errorBody}";
            }
            catch (HttpRequestException)
            {
                if (attempt == 3)
                {
                    return "The Gemini service could not be reached right now. Please try again shortly.";
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return "The request to Gemini was cancelled before completion.";
            }
        }

        return "The Gemini service is temporarily unavailable. Please try again shortly.";
    }
}
