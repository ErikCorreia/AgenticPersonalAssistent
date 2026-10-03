using System.Diagnostics;
using NAudio.Wave;

namespace Assistent.Speech;

public sealed class PiperTts : ITextToSpeech
{
    private readonly string _exe;
    private readonly string _model;

    public PiperTts(SpeechOptions options)
    {
        _exe = Resolve(options.PiperPath);
        _model = Resolve(options.PiperModelPath);
        if (!File.Exists(_exe)) throw new InvalidOperationException($"Piper não encontrado em '{_exe}'.");
        if (!File.Exists(_model)) throw new InvalidOperationException($"Modelo Piper não encontrado em '{_model}'.");
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var wav = Path.Combine(Path.GetTempPath(), $"myelin-{Guid.NewGuid():N}.wav");
        try
        {
            var psi = new ProcessStartInfo(_exe)
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_model);
            psi.ArgumentList.Add("--output_file");
            psi.ArgumentList.Add(wav);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar o Piper.");
            await process.StandardInput.WriteAsync(text.Replace('\n', ' '));
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(wav))
                throw new InvalidOperationException("O Piper não gerou áudio.");

            using var reader = new AudioFileReader(wav);
            using var output = new WaveOutEvent();
            output.Init(reader);
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            try { File.Delete(wav); } catch (IOException) { }
        }
    }

    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}