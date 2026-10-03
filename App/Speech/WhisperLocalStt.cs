using Whisper.net;

namespace Assistent.Speech;

public sealed class WhisperLocalStt(SpeechOptions options) : ISpeechToText, IDisposable
{
    private WhisperFactory? _factory;

    public async Task<string> ListenAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.IsPathRooted(options.WhisperModelPath)
            ? options.WhisperModelPath
            : Path.Combine(AppContext.BaseDirectory, options.WhisperModelPath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Modelo Whisper não encontrado em '{path}'. Baixe um modelo ggml (ex.: ggml-base.bin de huggingface.co/ggerganov/whisper.cpp) e ajuste Speech:WhisperModelPath.");
        }

        _factory ??= WhisperFactory.FromPath(path);
        var language = options.Language.Split('-')[0];
        using var processor = _factory.CreateBuilder().WithLanguage(language).Build();
        Console.WriteLine("[voz] Ouvindo... fale agora.");
        using var audio = await AudioRecorder.RecordAsync(options, cancellationToken);

        var text = new System.Text.StringBuilder();
        await foreach (var segment in processor.ProcessAsync(audio, cancellationToken))
        {
            text.Append(segment.Text);
        }
        return text.ToString().Trim();
    }

    public void Dispose() => _factory?.Dispose();
}