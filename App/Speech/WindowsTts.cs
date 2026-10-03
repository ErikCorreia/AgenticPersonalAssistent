using System.Speech.Synthesis;

namespace Assistent.Speech;

public sealed class WindowsTts : ITextToSpeech, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public WindowsTts(SpeechOptions options)
    {
        _synth.Rate = Math.Clamp(options.Rate, -10, 10);
        if (!string.IsNullOrWhiteSpace(options.Voice))
        {
            _synth.SelectVoice(options.Voice);
        }
        else
        {
            try { _synth.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, new System.Globalization.CultureInfo(options.Language)); }
            catch (ArgumentException) { }
        }
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        var prompt = _synth.SpeakAsync(text);
        void Done(object? s, SpeakCompletedEventArgs e) { _synth.SpeakCompleted -= Done; tcs.TrySetResult(); }
        _synth.SpeakCompleted += Done;
        cancellationToken.Register(() => { _synth.SpeakAsyncCancel(prompt); tcs.TrySetCanceled(); });
        return tcs.Task;
    }

    public void Dispose() => _synth.Dispose();
}