namespace Assistent.Speech;

public interface ISpeechToText
{
    Task<string> ListenAsync(CancellationToken cancellationToken = default);
}

public interface ITextToSpeech
{
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class SpeechOptions
{
    public string Language { get; init; } = "pt-BR";
    public string WhisperModelPath { get; init; } = "models\\ggml-base.bin";
    public string TtsProvider { get; init; } = "Windows";
    public string PiperPath { get; init; } = "piper\\piper.exe";
    public string PiperModelPath { get; init; } = "piper\\pt_BR-faber-medium.onnx";
    public string Voice { get; init; } = string.Empty;
    public int Rate { get; init; }
    public bool SpeakResponses { get; init; }
    public int MaxListenSeconds { get; init; } = 15;
    public int SilenceMilliseconds { get; init; } = 1200;
}