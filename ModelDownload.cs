using System.Diagnostics;

namespace GroqVoice;

/// <summary>
/// Fetches the on-device recognition model on demand. The model is ~460 MB, so
/// it is never shipped with the app and never committed — it lands in
/// %APPDATA%\GroqVoice\models\ the first time the user asks for it, and stays
/// there across updates.
///
/// Download goes to a .part file next to the target so an interrupted or
/// cancelled attempt can never be mistaken for a finished one; the archive is
/// only extracted once it is complete, and removed afterwards.
/// </summary>
public static class ModelDownload
{
    /// <summary>Directory name inside the archive — also the installed folder name.</summary>
    public const string ModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8";

    public const string Url =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" + ModelName + ".tar.bz2";

    /// <summary>Compressed size, for the "download 460 MB?" prompt.</summary>
    public const int DownloadMB = 464;

    public static string ModelsDir => Path.Combine(Config.Dir, "models");
    public static string ModelDir => Path.Combine(ModelsDir, ModelName);

    private static readonly string[] RequiredFiles =
        { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" };

    /// <summary>True when every file the recognizer needs is on disk.</summary>
    public static bool IsInstalled =>
        RequiredFiles.All(f => File.Exists(Path.Combine(ModelDir, f)));

    public static string PathOf(string file) => Path.Combine(ModelDir, file);

    /// <summary>Progress of a running download: 0..1, or -1 while extracting.</summary>
    public readonly record struct Progress(double Fraction, long BytesDone, long BytesTotal, bool Extracting);

    /// <summary>
    /// Downloads and extracts the model. <paramref name="onProgress"/> is called
    /// from a background thread — marshal to the UI yourself. Throws on failure;
    /// a cancelled download leaves nothing but the .part file behind.
    /// </summary>
    public static async Task InstallAsync(IProgress<Progress>? onProgress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDir);
        var archive = Path.Combine(ModelsDir, ModelName + ".tar.bz2");
        var partial = archive + ".part";

        var sw = Stopwatch.StartNew();
        Log.Info($"model download starting: {Url}");

        using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
        {
            using var resp = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct)
                                       .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? (long)DownloadMB * 1024 * 1024;

            using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using (var dst = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                                            1 << 20, useAsync: true))
            {
                var buf = new byte[1 << 20];
                long done = 0;
                int lastPercent = -1;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        onProgress?.Report(new Progress(total > 0 ? (double)done / total : 0, done, total, false));
                        if (percent % 10 == 0) Log.Info($"model download: {percent}%");
                    }
                }
            }
        }

        ct.ThrowIfCancellationRequested();
        File.Move(partial, archive, overwrite: true);
        Log.Info($"model downloaded in {sw.Elapsed.TotalSeconds:0}s, extracting…");
        onProgress?.Report(new Progress(-1, 0, 0, true));

        Extract(archive, ModelsDir, ct);
        try { File.Delete(archive); } catch { /* a leftover archive is harmless */ }

        if (!IsInstalled)
            throw new InvalidOperationException($"model files missing after extraction in {ModelDir}");
        Log.Info($"model ready in {ModelDir} ({sw.Elapsed.TotalSeconds:0}s total)");
    }

    /// <summary>
    /// Unpacks the .tar.bz2 with the tar.exe (bsdtar) that ships with Windows 10
    /// 1803 and later. Nothing else in the app needs an archive library, so this
    /// stays a one-line dependency on the OS rather than another NuGet package.
    /// </summary>
    private static void Extract(string archive, string intoDir, CancellationToken ct)
    {
        var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tar))
            throw new InvalidOperationException(
                "tar.exe not found in System32 — extract the archive manually into " + intoDir);

        var psi = new ProcessStartInfo(tar, $"-xjf \"{archive}\" -C \"{intoDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start tar.exe");
        using (ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch { } }))
        {
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            ct.ThrowIfCancellationRequested();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"tar exited {p.ExitCode}: {Truncate(err, 300)}");
        }
    }

    /// <summary>Deletes the installed model (menu action; frees ~660 MB).</summary>
    public static void Remove()
    {
        if (Directory.Exists(ModelDir)) Directory.Delete(ModelDir, recursive: true);
        Log.Info("local model removed");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
