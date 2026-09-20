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

        int p = Array.IndexOf(args, "--probe-selection");
        if (p >= 0)
        {
            int wait = p + 1 < args.Length && int.TryParse(args[p + 1], out var s) ? s : 5;
            Environment.ExitCode = ProbeSelection(wait);
            return;
        }

        int e = Array.IndexOf(args, "--edit");
        if (e >= 0 && e + 2 < args.Length) { Environment.ExitCode = EditSelection(args[e + 1], args[e + 2]); return; }

        if (args.Contains("--selftest-selection")) { Environment.ExitCode = SelfTestSelection(); return; }

        int u = Array.IndexOf(args, "--snapshot-ui");
        if (u >= 0) { Environment.ExitCode = SnapshotUi(u + 1 < args.Length ? args[u + 1] : "."); return; }

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

    /// <summary>
    /// `GroqVoice.exe --probe-selection [seconds]` — waits, then reports what the
    /// then-focused window hands over on Ctrl+C. Shows what the editing mode would
    /// see, without having to dictate into a real app.
    /// </summary>
    private static int ProbeSelection(int waitSeconds)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        Console.Out.WriteLine($"select some text somewhere — probing in {waitSeconds} s…");
        Console.Out.Flush();
        Thread.Sleep(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 0, 60)));

        Console.Out.WriteLine($"focused: {ForegroundApp.Describe()}");
        var text = Selection.TryRead();
        if (text is null) { Console.Out.WriteLine("selection: none"); return 1; }
        Console.Out.WriteLine($"selection ({text.Length} chars): {text}");
        return 0;
    }

    /// <summary>
    /// `GroqVoice.exe --selftest-selection` — checks the whole selection probe
    /// against a text box of our own: focus, Ctrl+C, clipboard read, restore.
    /// Uses our own window on purpose — driving synthetic keys into whatever the
    /// user happens to have focused is not something a test may do.
    /// </summary>
    private static int SelfTestSelection()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        const string Sample = "Selection probe sample — выделенный текст.";
        string? probed = null;
        string? clipboardBefore = Paster.ReadClipboardText();
        const string Sentinel = "GroqVoice self-test clipboard";

        ApplicationConfiguration.Initialize();
        Paster.WriteClipboardText(Sentinel);

        var form = new Form { Width = 420, Height = 160, Text = "GroqVoice self-test", TopMost = true };
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, Text = Sample };
        form.Controls.Add(box);
        form.Shown += async (_, _) =>
        {
            box.Focus();
            box.SelectAll();
            await Task.Delay(250);
            probed = await Task.Run(Selection.TryRead);
            form.Close();
        };
        Application.Run(form);

        string? clipboardAfter = Paster.ReadClipboardText();
        Paster.WriteClipboardText(clipboardBefore);

        Console.Out.WriteLine($"probed:    {probed ?? "(null)"}");
        Console.Out.WriteLine($"expected:  {Sample}");
        Console.Out.WriteLine($"restored:  {(clipboardAfter == Sentinel ? "yes" : $"NO — clipboard held \"{clipboardAfter}\"")}");

        bool ok = probed?.Trim() == Sample && clipboardAfter == Sentinel;
        Console.Out.WriteLine(ok ? "PASS" : "FAIL");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// `GroqVoice.exe --edit "selected text" "what was said"` — runs the editing
    /// stage without a microphone or a selection, to check the prompt and the model.
    /// </summary>
    private static int EditSelection(string selection, string spoken)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        try
        {
            var cfg = Config.Load();
            var groq = new Groq(cfg);
            var answer = groq.ChatAsync(Groq.EditSelectionUserMessage(selection, spoken),
                                        Groq.EditSelectionSystemPrompt)
                             .GetAwaiter().GetResult();
            Console.Out.WriteLine(answer.Trim());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// `GroqVoice.exe --snapshot-ui <dir>` — renders the Settings and History
    /// windows to PNGs and exits. Checks that they build and lay out, without a
    /// human opening every tab.
    /// </summary>
    private static int SnapshotUi(string dir)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        try
        {
            ApplicationConfiguration.Initialize();
            Directory.CreateDirectory(dir);
            var cfg = Config.Load();

            using (var settings = new SettingsForm(cfg))
                Shoot(settings, Path.Combine(dir, "settings.png"), tabs: true);

            var history = new History(cfg.HistorySize);
            using (var form = new HistoryForm(history, () => new PasteOptions(), false))
                Shoot(form, Path.Combine(dir, "history.png"));

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    /// <summary>Shows a window off-screen, renders it, and each of its tabs.</summary>
    private static void Shoot(Form form, string path, bool tabs = false)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-4000, -4000);
        form.Show();
        Application.DoEvents();

        var tabControl = form.Controls.OfType<TabControl>().FirstOrDefault();
        int pages = tabs && tabControl != null ? tabControl.TabPages.Count : 1;
        for (int i = 0; i < pages; i++)
        {
            if (tabControl != null && tabs) { tabControl.SelectedIndex = i; Application.DoEvents(); }
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            var name = pages > 1
                ? Path.Combine(Path.GetDirectoryName(path)!,
                               $"{Path.GetFileNameWithoutExtension(path)}-{i + 1}-{tabControl!.TabPages[i].Text}.png")
                : path;
            bmp.Save(name, System.Drawing.Imaging.ImageFormat.Png);
            Console.Out.WriteLine(name);
        }
        form.Hide();
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
