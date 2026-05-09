using System.Windows.Forms;

namespace GroqVoice;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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

        if (string.IsNullOrWhiteSpace(cfg.GroqApiKey))
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
}
