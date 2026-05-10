using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms;

namespace GroqVoice;

public sealed class TrayContext : ApplicationContext
{
    private readonly Config _cfg;
    private readonly Hotkey _hotkey;
    private readonly Recorder _rec = new();
    private readonly Groq _groq;
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _ui;

    private readonly Icon _idleIcon;
    private readonly Icon _recIcon;
    private readonly Icon _busyIcon;

    private CancellationTokenSource? _busyCts;
    private volatile bool _busy;

    public TrayContext(Config cfg)
    {
        _cfg = cfg;
        _groq = new Groq(cfg);
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _idleIcon = MakeDotIcon(Color.FromArgb(80, 200, 120));     // green
        _recIcon  = MakeDotIcon(Color.FromArgb(220, 60, 60));      // red
        _busyIcon = MakeDotIcon(Color.FromArgb(240, 180, 40));     // amber

        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            Text = "GroqVoice — hold Win+Ctrl to dictate",
            ContextMenuStrip = BuildMenu(),
        };

        Recorder.LogDevices(_cfg.InputDeviceContains);

        Vocabulary.EnsureFileExists();
        var (_, vocabCount) = Vocabulary.LoadPrompt();
        Log.Info($"vocabulary: {vocabCount} terms loaded from {Vocabulary.Path}");

        _hotkey = new Hotkey
        {
            PttHoldMs = Math.Max(50, _cfg.PttHoldMs),
            DoubleTapWindowMs = Math.Max(150, _cfg.DoubleTapWindowMs),
        };
        _hotkey.ChordPressed  += OnChordPressed;
        _hotkey.ChordReleased += OnChordReleased;
        _hotkey.ScreenshotTriggered += OnScreenshotTriggered;

        if (_cfg.Autostart) Autostart.Enable(Application.ExecutablePath);
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();

        m.Items.Add("GroqVoice — hold Win+Ctrl to dictate").Enabled = false;
        m.Items.Add(new ToolStripSeparator());

        var setup = new ToolStripMenuItem("Setup / change API key…");
        setup.Click += (_, _) =>
        {
            try
            {
                using var f = new SetupForm(_cfg);
                f.ShowDialog();
            }
            catch (Exception ex) { Log.Error("setup dialog failed", ex); ShowBalloon("Setup failed", ex.Message, ToolTipIcon.Error); }
        };
        m.Items.Add(setup);

        var openCfg = new ToolStripMenuItem("Open config (advanced)…");
        openCfg.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Config.Path}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("open config failed", ex); ShowBalloon("Failed to open config", ex.Message, ToolTipIcon.Error); }
        };
        m.Items.Add(openCfg);

        var openVocab = new ToolStripMenuItem("Open vocabulary…");
        openVocab.Click += (_, _) =>
        {
            try
            {
                Vocabulary.EnsureFileExists();
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Vocabulary.Path}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("open vocabulary failed", ex); ShowBalloon("Failed to open vocabulary", ex.Message, ToolTipIcon.Error); }
        };
        m.Items.Add(openVocab);

        var openLog = new ToolStripMenuItem("Open log");
        openLog.Click += (_, _) =>
        {
            try
            {
                if (!File.Exists(Log.Path)) File.WriteAllText(Log.Path, "");
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Log.Path}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { ShowBalloon("Failed to open log", ex.Message, ToolTipIcon.Error); }
        };
        m.Items.Add(openLog);

        var openFolder = new ToolStripMenuItem("Open data folder");
        openFolder.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Config.Dir}\"") { UseShellExecute = true }); }
            catch { }
        };
        m.Items.Add(openFolder);

        var mics = new ToolStripMenuItem("Microphone");
        mics.DropDownOpening += (_, _) => RebuildMicMenu(mics);
        m.Items.Add(mics);

        var auto = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = Autostart.IsEnabled() };
        auto.CheckedChanged += (_, _) =>
        {
            try
            {
                if (auto.Checked) Autostart.Enable(Application.ExecutablePath);
                else Autostart.Disable();
                _cfg.Autostart = auto.Checked;
                _cfg.Save();
            }
            catch (Exception ex) { Log.Error("autostart toggle failed", ex); ShowBalloon("Autostart change failed", ex.Message, ToolTipIcon.Error); }
        };
        m.Items.Add(auto);

        m.Items.Add(new ToolStripSeparator());
        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => ExitThread();
        m.Items.Add(quit);

        return m;
    }

    private void RebuildMicMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var def = new ToolStripMenuItem("(system default)")
        {
            Checked = string.IsNullOrWhiteSpace(_cfg.InputDeviceContains),
        };
        def.Click += (_, _) => SelectMic("");
        parent.DropDownItems.Add(def);
        parent.DropDownItems.Add(new ToolStripSeparator());

        var names = Recorder.ListDeviceNames();
        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var item = new ToolStripMenuItem(name)
            {
                Checked = !string.IsNullOrWhiteSpace(_cfg.InputDeviceContains)
                    && name.Contains(_cfg.InputDeviceContains, StringComparison.OrdinalIgnoreCase),
            };
            string captured = name;
            item.Click += (_, _) => SelectMic(captured);
            parent.DropDownItems.Add(item);
        }
    }

    private void SelectMic(string nameContains)
    {
        _cfg.InputDeviceContains = nameContains;
        _cfg.Save();
        Log.Info($"mic switched via tray: '{(string.IsNullOrEmpty(nameContains) ? "(default)" : nameContains)}'");
        ShowBalloon("Microphone changed",
            string.IsNullOrEmpty(nameContains) ? "Now using system default input." : $"Now using: {nameContains}",
            ToolTipIcon.Info);
    }

    private void OnScreenshotTriggered()
    {
        // Hook callback runs on the UI thread already, but defer the modal
        // dialog to the next message-pump tick so we return from the hook
        // promptly (Windows unhooks slow callbacks).
        _ui.Post(_ => RunSnipping(), null);
    }

    private void RunSnipping()
    {
        if (_busy) Log.Info("snip triggered while busy — proceeding anyway");
        Log.Info("screenshot snip: opening overlay");
        Bitmap? cropped = null;
        try
        {
            using var form = new SnippingForm();
            var dr = form.ShowDialog();
            if (dr == DialogResult.OK && form.Result != null)
            {
                cropped = (Bitmap)form.Result.Clone();
            }
        }
        catch (Exception ex) { Log.Error("snip overlay failed", ex); ShowBalloon("Snip failed", ex.Message, ToolTipIcon.Error); return; }

        if (cropped == null)
        {
            Log.Info("snip cancelled");
            return;
        }

        try
        {
            // Clipboard requires STA; UI thread is STA. Set both Bitmap and PNG for compatibility.
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, cropped);
            using (var ms = new MemoryStream())
            {
                cropped.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                data.SetData("PNG", false, ms.ToArray());
            }
            Clipboard.SetDataObject(data, copy: true);
            Log.Info($"snip copied to clipboard: {cropped.Width}×{cropped.Height}");
            if (_cfg.PlayFeedbackSounds) Click.Low();
        }
        catch (Exception ex)
        {
            Log.Error("clipboard set image failed", ex);
            ShowBalloon("Snip clipboard error", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            cropped.Dispose();
        }
    }

    private void OnChordPressed()
    {
        if (_busy) { Log.Info("chord pressed but busy, ignoring"); return; }
        try
        {
            _rec.Start(_cfg.InputDeviceContains);
            Log.Info("recording started");
            _ui.Post(_ => { _tray.Icon = _recIcon; _tray.Text = "GroqVoice — recording…"; }, null);
            if (_cfg.PlayFeedbackSounds) Click.High();
        }
        catch (Exception ex)
        {
            Log.Error("mic start failed", ex);
            _ui.Post(_ => ShowBalloon("Mic error", ex.Message, ToolTipIcon.Error), null);
        }
    }

    private void OnChordReleased(bool clean)
    {
        if (!_rec.IsRecording) return;
        byte[]? wav = null;
        try { wav = _rec.Stop(); }
        catch (Exception ex)
        {
            Log.Error("recorder stop failed", ex);
            _ui.Post(_ => ShowBalloon("Recorder error", ex.Message, ToolTipIcon.Error), null);
        }

        if (!clean)
        {
            Log.Info("chord released with third-key dirty flag — discarding audio");
            _ui.Post(_ => { _tray.Icon = _idleIcon; _tray.Text = "GroqVoice — ready"; }, null);
            return;
        }

        if (wav is null)
        {
            Log.Info("no audio captured, skipping");
            _ui.Post(_ => { _tray.Icon = _idleIcon; _tray.Text = "GroqVoice — ready"; }, null);
            return;
        }

        // 44-byte WAV header, 16 kHz mono 16-bit = 32000 bytes/sec
        const int BytesPerSec = 16000 * 2;
        double seconds = (double)Math.Max(0, wav.Length - 44) / BytesPerSec;
        int peak = _rec.LastPeakAbs;
        double peakPct = peak * 100.0 / 32768.0;
        Log.Info($"recording stopped: {seconds:0.00}s, {wav.Length} bytes, peak={peak} ({peakPct:0.00}%)");

        if (seconds < _cfg.MinRecordingSeconds)
        {
            Log.Info($"too short ({seconds:0.00}s < {_cfg.MinRecordingSeconds:0.00}s) — not sending to Groq");
            _ui.Post(_ => { _tray.Icon = _idleIcon; _tray.Text = "GroqVoice — ready"; }, null);
            return;
        }
        if (peakPct < _cfg.SilencePeakPercent)
        {
            Log.Info($"silent (peak {peakPct:0.00}% < {_cfg.SilencePeakPercent:0.00}%) — not sending to Groq");
            _ui.Post(_ => { _tray.Icon = _idleIcon; _tray.Text = "GroqVoice — ready"; }, null);
            return;
        }

        _ui.Post(_ => { _tray.Icon = _busyIcon; _tray.Text = "GroqVoice — processing…"; }, null);

        if (_cfg.SaveLastWav)
        {
            try
            {
                var wavPath = System.IO.Path.Combine(Config.Dir, "last.wav");
                File.WriteAllBytes(wavPath, wav);
                Log.Info($"saved debug wav to {wavPath}");
            }
            catch (Exception ex) { Log.Warn($"could not save last.wav: {ex.Message}"); }
        }

        _busy = true;
        _busyCts = new CancellationTokenSource();
        var ct = _busyCts.Token;

        // fire-and-forget pipeline; UI updates marshalled via _ui.Post
        _ = Task.Run(async () =>
        {
            try
            {
                var (vocabPrompt, vocabCount) = Vocabulary.LoadPrompt();
                if (vocabCount > 0) Log.Info($"vocabulary: {vocabCount} terms ({vocabPrompt.Length} chars) sent as Whisper prompt");
                var transcript = await _groq.TranscribeAsync(wav, vocabPrompt, ct).ConfigureAwait(false);
                Log.Info($"STT result: \"{Snip(transcript)}\"");
                if (string.IsNullOrWhiteSpace(transcript)) return;

                bool isTask = LeadsWithTaskKeyword(transcript, _cfg.TaskKeywords, _cfg.TaskKeywordMaxWordPosition, out var stripped);
                string output;

                if (isTask)
                {
                    Log.Info($"task mode → chat: \"{Snip(stripped)}\"");
                    output = await _groq.ChatAsync(stripped, ct).ConfigureAwait(false);
                    output = output.Trim();
                    Log.Info($"chat result: \"{Snip(output)}\"");
                }
                else
                {
                    output = transcript.Trim();
                }

                if (!string.IsNullOrEmpty(output))
                    _ui.Post(_ => Paster.Paste(output), null);

                if (_cfg.PlayFeedbackSounds) Click.Low();
            }
            catch (Exception ex)
            {
                Log.Error("groq pipeline failed", ex);
                _ui.Post(_ => ShowBalloon("Groq error", ex.Message, ToolTipIcon.Error), null);
            }
            finally
            {
                _busy = false;
                _busyCts?.Dispose();
                _busyCts = null;
                _ui.Post(_ => { _tray.Icon = _idleIcon; _tray.Text = "GroqVoice — ready"; }, null);
            }
        });
    }

    private static string Snip(string s, int n = 200) =>
        s.Length <= n ? s.Replace("\r", " ").Replace("\n", " ⏎ ")
                       : s[..n].Replace("\r", " ").Replace("\n", " ⏎ ") + "…";

    /// <summary>
    /// Returns true if any keyword (case-insensitive whole-word match) appears
    /// among the first <paramref name="maxWords"/> words of the transcript.
    /// Out parameter <paramref name="remainder"/> is everything AFTER the matched keyword,
    /// with leading separators trimmed.
    /// </summary>
    private static bool LeadsWithTaskKeyword(string text, string[] keywords, int maxWords, out string remainder)
    {
        remainder = text;
        if (string.IsNullOrWhiteSpace(text) || maxWords <= 0) return false;

        int pos = 0;
        int len = text.Length;
        int wordIdx = 0;

        while (pos < len && wordIdx < maxWords)
        {
            // skip whitespace + punctuation between words
            while (pos < len && !IsWordChar(text[pos])) pos++;
            if (pos >= len) break;

            int wStart = pos;
            while (pos < len && IsWordChar(text[pos])) pos++;
            int wEnd = pos;
            wordIdx++;

            ReadOnlySpan<char> word = text.AsSpan(wStart, wEnd - wStart);
            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                if (word.Equals(kw.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    // remainder begins after this word, skipping any "  : , — - ." filler
                    int r = wEnd;
                    while (r < len && (char.IsWhiteSpace(text[r]) || ":,.;—-".IndexOf(text[r]) >= 0))
                        r++;
                    remainder = text.Substring(r);
                    return remainder.Length > 0;
                }
            }
        }
        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'';

    private void ShowBalloon(string title, string body, ToolTipIcon icon)
    {
        try { _tray.ShowBalloonTip(4000, title, body, icon); } catch { }
    }

    private static Icon MakeDotIcon(Color c)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(c);
            g.FillEllipse(b, 2, 2, 12, 12);
            using var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1);
            g.DrawEllipse(pen, 2, 2, 12, 12);
        }
        var hIcon = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { DestroyIcon(hIcon); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _busyCts?.Cancel(); } catch { }
            _hotkey.Dispose();
            _rec.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _idleIcon.Dispose();
            _recIcon.Dispose();
            _busyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
