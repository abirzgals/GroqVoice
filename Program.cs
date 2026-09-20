using System.Windows.Forms;

namespace GroqVoice;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Headless: transcribe a WAV with the on-device engine and print it.
        // Runs before the single-instance mutex so it works while the tray app is up.
        int t = Array.IndexOf(args, "--transcribe");
        if (t >= 0 && t + 1 < args.Length) { Environment.ExitCode = Transcribe(args[t + 1]); return; }
        if (args.Contains("--download-model")) { Environment.ExitCode = DownloadModel(); return; }

        using var mutex = new Mutex(initiallyOwned: true, name: "Global\\GroqVoice.SingleInstance", out bool created);
        if (!created) { Log.Info("another instance already running, exiting"); return; }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("AppDomain unhandled", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("unobserved task", e.Exception); e.SetObserved(); };

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Log.Info($"GroqVoice starting. exe={Application.ExecutablePath}");
        var cfg = Config.Load();
        Log.Info($"config loaded. apiKey={(string.IsNullOrWhiteSpace(cfg.GroqApiKey) ? "MISSING" : "set")} sttModel={cfg.TranscriptionModel} chatModel={cfg.ChatModel}");

        // The key is only needed for the cloud engine and for task mode. With
        // Parakeet downloaded, dictation works without an account — don't block
        // startup on a key the user may never need.
        if (string.IsNullOrWhiteSpace(cfg.GroqApiKey) && cfg.UsesLocalEngine && ModelDownload.IsInstalled)
        {
            Log.Info("no API key, but the local engine is ready — skipping setup");
        }
        else if (string.IsNullOrWhiteSpace(cfg.GroqApiKey))
        {
            Log.Info("no API key — showing first-run setup dialog");
            using var setup = new SetupForm(cfg);
            var dr = setup.ShowDialog();
            if (!setup.Saved)
            {
                Log.Info("setup dialog dismissed without a key — exiting");
                return;
            }
            // SetupForm mutates the same Config instance and persists it; nothing else to reload.
        }

        using var ctx = new TrayContext(cfg);
        Application.Run(ctx);
        Log.Info("GroqVoice exited.");

        GC.KeepAlive(mutex);
    }

    /// <summary>
    /// `GroqVoice.exe --transcribe file.wav` — recognizes a 16 kHz mono WAV with
    /// the local model and prints the text. Used to check the engine and the
    /// vocabulary without speaking into the tray app.
    /// </summary>
    private static int Transcribe(string wavPath)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        try
        {
            if (!File.Exists(wavPath)) { Console.Error.WriteLine($"no such file: {wavPath}"); return 2; }
            if (!ModelDownload.IsInstalled)
            {
                Console.Error.WriteLine($"local model not downloaded ({ModelDownload.ModelDir})");
                return 3;
            }

            using var stt = new LocalStt();
            var text = Vocabulary.ApplyAliases(stt.Transcribe(File.ReadAllBytes(wavPath)));
            Console.Out.WriteLine(text);
            Console.Out.Flush();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("--transcribe failed", ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// `GroqVoice.exe --download-model` — fetches the local model from a terminal,
    /// printing percentages. The tray menu does the same thing in a window; this is
    /// for scripted installs and for checking the download without the UI.
    /// </summary>
    private static int DownloadModel()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        if (ModelDownload.IsInstalled)
        {
            Console.Out.WriteLine($"already installed: {ModelDownload.ModelDir}");
            return 0;
        }
        try
        {
            int last = -1;
            var progress = new Progress<ModelDownload.Progress>(p =>
            {
                if (p.Extracting) { Console.Out.WriteLine("\nunpacking…"); return; }
                int percent = (int)(p.Fraction * 100);
                if (percent == last) return;
                last = percent;
                Console.Out.Write($"\rdownloading {percent,3}%  ({p.BytesDone / 1048576} / {p.BytesTotal / 1048576} MB)");
                Console.Out.Flush();
            });
            ModelDownload.InstallAsync(progress, CancellationToken.None).GetAwaiter().GetResult();
            Console.Out.WriteLine($"\nready: {ModelDownload.ModelDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("--download-model failed", ex);
            Console.Error.WriteLine($"\n{ex.Message}");
            return 1;
        }
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
