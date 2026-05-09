using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GroqVoice;

/// <summary>
/// WASAPI shared-mode capture. The device is shared with other apps —
/// nothing is held open between recordings, and even during a recording the
/// mic is not exclusive (Discord, Zoom, OBS, etc. can all read the same stream
/// in parallel). We capture at the device's native mix format and only resample
/// to 16 kHz mono int16 on Stop(), so the hot path is light.
/// </summary>
public sealed class Recorder : IDisposable
{
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private MMDeviceEnumerator? _enumerator;
    private MemoryStream? _native;
    private WaveFormat? _nativeFormat;
    private int _peakAbs;
    private bool _running;

    public bool IsRecording => _running;
    /// <summary>Peak absolute amplitude scaled to 0..32768, computed in native format.</summary>
    public int LastPeakAbs => _peakAbs;

    /// <summary>Logs the available capture devices and which one would be picked given the name filter.</summary>
    public static void LogDevices(string? nameContains)
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            var devs = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            Log.Info($"WASAPI capture devices: count={devs.Count}");
            for (int i = 0; i < devs.Count; i++)
            {
                var d = devs[i];
                Log.Info($"  [{i}] {d.FriendlyName}");
            }
            try
            {
                var def = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                Log.Info($"  default (communications): {def.FriendlyName}");
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(nameContains))
                Log.Info($"  filter: FriendlyName contains \"{nameContains}\"");
        }
        catch (Exception ex) { Log.Warn($"WASAPI device enum failed: {ex.Message}"); }
    }

    /// <summary>Returns FriendlyNames of all active capture endpoints.</summary>
    public static List<string> ListDeviceNames()
    {
        var names = new List<string>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                names.Add(d.FriendlyName);
        }
        catch (Exception ex) { Log.Warn($"WASAPI list failed: {ex.Message}"); }
        return names;
    }

    /// <summary>Opens the matching (or default communications) device and starts shared-mode capture.</summary>
    public void Start(string? nameContains)
    {
        if (_running) return;
        _peakAbs = 0;
        _native = new MemoryStream();
        _enumerator = new MMDeviceEnumerator();
        _device = ResolveDevice(_enumerator, nameContains);
        // WasapiCapture defaults to shared mode — what we want.
        _capture = new WasapiCapture(_device, useEventSync: false, audioBufferMillisecondsLength: 100);
        _nativeFormat = _capture.WaveFormat;
        Log.Info($"WASAPI start: {_device.FriendlyName} | {_nativeFormat.SampleRate} Hz, {_nativeFormat.Channels} ch, {_nativeFormat.Encoding}, {_nativeFormat.BitsPerSample}-bit");
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null) Log.Error("WASAPI stopped with error", e.Exception);
        };
        _capture.StartRecording();
        _running = true;
    }

    private static MMDevice ResolveDevice(MMDeviceEnumerator en, string? nameContains)
    {
        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (d.FriendlyName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            Log.Warn($"no capture device matched \"{nameContains}\" — falling back to default");
        }
        return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _native!.Write(e.Buffer, 0, e.BytesRecorded);
        UpdatePeak(e.Buffer, e.BytesRecorded);
    }

    private void UpdatePeak(byte[] buf, int bytes)
    {
        var f = _nativeFormat;
        if (f == null) return;

        if (f.Encoding == WaveFormatEncoding.IeeeFloat && f.BitsPerSample == 32)
        {
            int n = bytes / 4;
            for (int i = 0; i < n; i++)
            {
                float s = BitConverter.ToSingle(buf, i * 4);
                int abs = (int)(MathF.Abs(s) * 32768);
                if (abs > 32768) abs = 32768;
                if (abs > _peakAbs) _peakAbs = abs;
            }
        }
        else if (f.Encoding == WaveFormatEncoding.Pcm && f.BitsPerSample == 16)
        {
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                short s = (short)(buf[i] | (buf[i + 1] << 8));
                int abs = s == short.MinValue ? 32768 : Math.Abs(s);
                if (abs > _peakAbs) _peakAbs = abs;
            }
        }
        // 24-bit / other formats: peak is left at 0 — minor inconvenience, not a blocker.
    }

    /// <summary>Returns a 16 kHz / 16-bit / mono WAV (RIFF) of the captured audio, or null if too short.</summary>
    public byte[]? Stop()
    {
        if (!_running) return null;
        _running = false;
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose(); _capture = null;
        _device?.Dispose(); _device = null;
        _enumerator?.Dispose(); _enumerator = null;

        var native = _native?.ToArray() ?? Array.Empty<byte>();
        _native?.Dispose(); _native = null;
        var fmt = _nativeFormat;
        _nativeFormat = null;
        if (fmt == null) return null;

        int minBytes = fmt.AverageBytesPerSecond / 4; // ≥ 250 ms of native audio
        if (native.Length < minBytes) return null;

        return ConvertTo16kMonoWav(native, fmt);
    }

    private static byte[] ConvertTo16kMonoWav(byte[] data, WaveFormat src)
    {
        using var inMs = new MemoryStream(data);
        using var raw = new RawSourceWaveStream(inMs, src);
        ISampleProvider sp = raw.ToSampleProvider();
        if (src.Channels > 1) sp = sp.ToMono();
        if (sp.WaveFormat.SampleRate != 16000)
            sp = new WdlResamplingSampleProvider(sp, 16000);
        var wp = new SampleToWaveProvider16(sp);

        using var pcmMs = new MemoryStream();
        var buf = new byte[4096];
        int n;
        while ((n = wp.Read(buf, 0, buf.Length)) > 0) pcmMs.Write(buf, 0, n);
        return WrapWav(pcmMs.ToArray(), wp.WaveFormat);
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
            bw.Write((short)1);                // PCM
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
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
        _device?.Dispose();
        _enumerator?.Dispose();
        _native?.Dispose();
    }
}
