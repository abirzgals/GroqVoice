using System.Diagnostics;
using SherpaOnnx;

namespace GroqVoice;

/// <summary>
/// On-device recognition with NVIDIA Parakeet TDT v3 (sherpa-onnx / ONNX Runtime,
/// CPU). Offline, no API key, 25 languages including Russian and English mixed
/// inside one phrase — measured here at ~0.35 s for 4.4 s of speech.
///
/// Loading the model costs ~2.4 s and ~700 MB of RAM, so it is loaded once and kept
/// warm; <see cref="Config.LocalUnloadAfterMinutes"/> drops it again after an idle
/// stretch for people who would rather have the memory back.
/// </summary>
public sealed class LocalStt : IDisposable
{
    private readonly object _gate = new();
    private OfflineRecognizer? _rec;
    private System.Threading.Timer? _unloadTimer;
    private double _unloadAfterMinutes;

    /// <summary>Set once at startup and whenever the setting changes.</summary>
    public void SetUnloadAfterMinutes(double minutes)
    {
        _unloadAfterMinutes = minutes;
        if (minutes <= 0) { _unloadTimer?.Dispose(); _unloadTimer = null; }
    }

    public bool IsModelInstalled => ModelDownload.IsInstalled;
    public bool IsLoaded { get { lock (_gate) return _rec != null; } }

    /// <summary>Loads the model if it isn't loaded yet. Blocking; call off the UI thread.</summary>
    public void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_rec != null) return;
            if (!ModelDownload.IsInstalled)
                throw new InvalidOperationException(
                    "local model is not downloaded — pick \"Download model…\" in the tray menu");

            var sw = Stopwatch.StartNew();
            var cfg = new OfflineRecognizerConfig();
            cfg.ModelConfig.Transducer.Encoder = ModelDownload.PathOf("encoder.int8.onnx");
            cfg.ModelConfig.Transducer.Decoder = ModelDownload.PathOf("decoder.int8.onnx");
            cfg.ModelConfig.Transducer.Joiner = ModelDownload.PathOf("joiner.int8.onnx");
            cfg.ModelConfig.Tokens = ModelDownload.PathOf("tokens.txt");
            cfg.ModelConfig.ModelType = "nemo_transducer";
            // Past four threads the encoder gains nothing and starts competing
            // with whatever the user is actually doing.
            cfg.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
            cfg.ModelConfig.Debug = 0;
            cfg.DecodingMethod = "greedy_search";

            _rec = new OfflineRecognizer(cfg);
            Log.Info($"local STT: Parakeet v3 loaded in {sw.ElapsedMilliseconds} ms " +
                     $"({cfg.ModelConfig.NumThreads} threads)");
        }
    }

    /// <summary>Loads the model in the background so the first dictation isn't slow.</summary>
    public void WarmUpInBackground()
    {
        if (IsLoaded || !ModelDownload.IsInstalled) return;
        Task.Run(() =>
        {
            try { EnsureLoaded(); }
            catch (Exception ex) { Log.Warn($"local STT warm-up failed: {ex.Message}"); }
        });
    }

    /// <summary>
    /// Transcribes a 16 kHz mono 16-bit WAV as produced by <see cref="Recorder"/>.
    /// A fixed language would only filter tokens by script, which is exactly wrong
    /// for mixed Russian/English speech, so the language setting is not applied here.
    /// </summary>
    public string Transcribe(byte[] wav, CancellationToken ct = default)
    {
        EnsureLoaded();
        ct.ThrowIfCancellationRequested();

        float[] samples = PcmToFloat(wav);
        var sw = Stopwatch.StartNew();
        string text;
        lock (_gate)
        {
            var rec = _rec ?? throw new InvalidOperationException("recognizer unloaded mid-flight");
            using var stream = rec.CreateStream();
            stream.AcceptWaveform(16000, samples);
            rec.Decode(stream);
            text = stream.Result.Text ?? "";
        }
        Log.Info($"local STT: {samples.Length / 16000.0:0.00}s of audio in {sw.ElapsedMilliseconds} ms");

        ScheduleUnload();
        return text.Trim();
    }

    /// <summary>Strips the 44-byte RIFF header and scales int16 samples to -1..1.</summary>
    private static float[] PcmToFloat(byte[] wav)
    {
        const int HeaderBytes = 44;
        int n = Math.Max(0, (wav.Length - HeaderBytes) / 2);
        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            int o = HeaderBytes + i * 2;
            samples[i] = (short)(wav[o] | (wav[o + 1] << 8)) / 32768f;
        }
        return samples;
    }

    private void ScheduleUnload()
    {
        if (_unloadAfterMinutes <= 0) return;
        var due = TimeSpan.FromMinutes(_unloadAfterMinutes);
        if (_unloadTimer == null)
            _unloadTimer = new System.Threading.Timer(_ => Unload(), null, due, Timeout.InfiniteTimeSpan);
        else
            _unloadTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    public void Unload()
    {
        lock (_gate)
        {
            if (_rec == null) return;
            _rec.Dispose();
            _rec = null;
            Log.Info("local STT: model unloaded (idle)");
        }
        GC.Collect();
    }

    public void Dispose()
    {
        _unloadTimer?.Dispose();
        _unloadTimer = null;
        Unload();
    }
}
