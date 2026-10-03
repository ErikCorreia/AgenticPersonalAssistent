using NAudio.Wave;

namespace Assistent.Speech;

public static class AudioRecorder
{
    // Grava 16 kHz mono 16-bit (formato esperado pelo Whisper) e para ao detectar silêncio.
    public static async Task<MemoryStream> RecordAsync(SpeechOptions options, CancellationToken cancellationToken)
    {
        var format = new WaveFormat(16000, 16, 1);
        var output = new MemoryStream();
        var writer = new WaveFileWriter(new IgnoreDisposeStream(output), format);
        var done = new TaskCompletionSource();
        var started = DateTime.UtcNow;
        var lastVoice = DateTime.UtcNow;
        var heardVoice = false;

        using var waveIn = new WaveInEvent { WaveFormat = format, BufferMilliseconds = 100 };
        waveIn.DataAvailable += (_, e) =>
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
            var now = DateTime.UtcNow;
            if (Rms(e.Buffer, e.BytesRecorded) > 0.02)
            {
                heardVoice = true;
                lastVoice = now;
            }
            var silent = heardVoice && (now - lastVoice).TotalMilliseconds > options.SilenceMilliseconds;
            var timeout = (now - started).TotalSeconds > options.MaxListenSeconds
                || (!heardVoice && (now - started).TotalSeconds > 6);
            if (silent || timeout) done.TrySetResult();
        };
        waveIn.RecordingStopped += (_, _) => done.TrySetResult();

        using var registration = cancellationToken.Register(() => done.TrySetCanceled());
        waveIn.StartRecording();
        try { await done.Task; }
        finally { waveIn.StopRecording(); writer.Dispose(); }

        output.Position = 0;
        return output;
    }

    internal static double Rms(byte[] buffer, int count)
    {
        double sum = 0;
        var samples = count / 2;
        if (samples == 0) return 0;
        for (var i = 0; i < samples; i++)
        {
            var s = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            sum += s * s;
        }
        return Math.Sqrt(sum / samples);
    }

    private sealed class IgnoreDisposeStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override long Seek(long o, SeekOrigin so) => inner.Seek(o, so);
        public override void SetLength(long v) => inner.SetLength(v);
        public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
        protected override void Dispose(bool disposing) { }
    }
}