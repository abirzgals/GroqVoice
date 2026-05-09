using NAudio.Wave;

namespace GroqVoice;

/// <summary>16 kHz / 16-bit / mono PCM capture into a memory WAV stream.</summary>
public sealed class Recorder : IDisposable
{
    private WaveInEvent? _wave;
    private MemoryStream? _pcm;
    private WaveFormat _format = new WaveFormat(16000, 16, 1);
    private bool _running;
    private int _peakAbs;
    public bool IsRecording => _running;
    public int LastPeakAbs => _peakAbs;

    /// <summary>Resolves which device index to capture from given an optional substring match.
    /// Returns -1 (system default) if no match. Logs the full device list.</summary>
    public static int ResolveDevice(string? nameContains)
    {
        int count = WaveInEvent.DeviceCount;
        Log.Info($"WaveIn devices: count={count}");
        int chosen = -1; // -1 = WAVE_MAPPER (system default)
        for (int i = 0; i < count; i++)
        {
            try
            {
                var caps = WaveInEvent.GetCapabilities(i);
                Log.Info($"  [{i}] {caps.ProductName} ch={caps.Channels}");
                if (chosen == -1 && !string.IsNullOrWhiteSpace(nameContains)
                    && caps.ProductName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = i;
                }
            }
            catch (Exception ex) { Log.Warn($"  [{i}] caps failed: {ex.Message}"); }
        }
        Log.Info($"WaveIn selected: {(chosen == -1 ? "WAVE_MAPPER (system default)" : chosen.ToString())}");
        return chosen;
    }

    public void Start(int deviceIndex)
    {
        if (_running) return;
        _peakAbs = 0;
        _pcm = new MemoryStream();
        _wave = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            WaveFormat = _format,
            BufferMilliseconds = 50,
            NumberOfBuffers = 3,
        };
        _wave.DataAvailable += OnData;
        _wave.RecordingStopped += (_, e) => { if (e.Exception != null) Log.Error("WaveIn stopped with error", e.Exception); };
        _wave.StartRecording();
        _running = true;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _pcm!.Write(e.Buffer, 0, e.BytesRecorded);
        // peak as absolute 16-bit value (max 32768)
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            int abs = s == short.MinValue ? 32768 : Math.Abs(s);
            if (abs > _peakAbs) _peakAbs = abs;
        }
    }

    /// <summary>Returns a complete WAV (RIFF) payload, or null if too short.</summary>
    public byte[]? Stop()
    {
        if (!_running) return null;
        _running = false;
        try { _wave?.StopRecording(); } catch { }
        _wave?.Dispose();
        _wave = null;

        var pcm = _pcm?.ToArray() ?? Array.Empty<byte>();
        _pcm?.Dispose();
        _pcm = null;

        int minBytes = _format.AverageBytesPerSecond / 4; // 250 ms minimum
        if (pcm.Length < minBytes) return null;

        return WrapWav(pcm, _format);
    }

    private static byte[] WrapWav(byte[] pcm, WaveFormat fmt)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + pcm.Length);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)fmt.Channels);
            bw.Write(fmt.SampleRate);
            bw.Write(fmt.AverageBytesPerSecond);
            bw.Write((short)fmt.BlockAlign);
            bw.Write((short)fmt.BitsPerSample);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(pcm.Length);
            bw.Write(pcm);
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        try { _wave?.StopRecording(); } catch { }
        _wave?.Dispose();
        _pcm?.Dispose();
    }
}
