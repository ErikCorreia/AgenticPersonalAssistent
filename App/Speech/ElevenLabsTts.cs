using System.Net.Http.Json;
using NAudio.Wave;

namespace Assistent.Speech;

public sealed class ElevenLabsTts : ITextToSpeech
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SpeechOptions _options;
    private readonly string _apiKey;

    public ElevenLabsTts(SpeechOptions options)
    {
        _options = options;
        _apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Defina a variável de ambiente ELEVENLABS_API_KEY.");
        if (string.IsNullOrWhiteSpace(options.ElevenLabsVoiceId))
            throw new InvalidOperationException("Configure Speech:ElevenLabsVoiceId.");
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(_options.ElevenLabsVoiceId)}?output_format=mp3_44100_128";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { text, model_id = _options.ElevenLabsModel })
        };
        request.Headers.Add("xi-api-key", _apiKey);
        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ElevenLabs retornou HTTP {(int)response.StatusCode}.");

        var mp3 = Path.Combine(Path.GetTempPath(), $"myelin-{Guid.NewGuid():N}.mp3");
        try
        {
            await File.WriteAllBytesAsync(mp3, await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
            using var reader = new AudioFileReader(mp3);
            using var output = new WaveOutEvent();
            output.Init(reader);
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing)
                await Task.Delay(100, cancellationToken);
        }
        finally
        {
            try { File.Delete(mp3); } catch (IOException) { }
        }
    }
}