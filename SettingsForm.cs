using System.Drawing;

namespace GroqVoice;

/// <summary>
/// Everything in config.json that is worth changing without a text editor, in
/// four tabs. Edits apply as they are made — there is no OK button — and each
/// one is handed to the app through <see cref="Changed"/> so the running hotkeys,
/// engine and recorder pick it up immediately.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly Config _cfg;

    /// <summary>Raised after every change, once the config has been saved.</summary>
    public event Action? Changed;

    /// <summary>Asked to run the model download flow (Recognition tab).</summary>
    public event Action? ModelRequested;

    private readonly Label _modelStatus = new();
    private readonly Button _modelButton = new();
    private bool _loading = true;

    public SettingsForm(Config cfg)
    {
        _cfg = cfg;

        Text = "GroqVoice — Settings";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 520);
        MinimumSize = new Size(560, 480);
        Font = new Font("Segoe UI", 9.5f);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        tabs.TabPages.Add(BuildGeneral());
        tabs.TabPages.Add(BuildRecognition());
        tabs.TabPages.Add(BuildPaste());
        tabs.TabPages.Add(BuildLlm());

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(10, 8, 10, 8),
        };
        var close = new Button { Text = "Close", Width = 90 };
        close.Click += (_, _) => Close();
        var openConfig = new Button { Text = "Open config.json", Width = 140 };
        openConfig.Click += (_, _) => OpenInEditor(Config.Path);
        var openLog = new Button { Text = "Open log", Width = 100 };
        openLog.Click += (_, _) => OpenInEditor(Log.Path);
        footer.Controls.AddRange(new Control[] { close, openConfig, openLog });

        Controls.Add(tabs);
        Controls.Add(footer);

        _loading = false;
        RefreshModelStatus();
    }

    // ---------------- tabs ----------------

    private TabPage BuildGeneral()
    {
        var page = NewPage("General");
        var rows = NewRows(page);

        AddCheck(rows, "Start with Windows", _cfg.Autostart, v =>
        {
            _cfg.Autostart = v;
            if (v) Autostart.Enable(Application.ExecutablePath); else Autostart.Disable();
        });
        AddCheck(rows, "Play a sound when recording starts and stops", _cfg.PlayFeedbackSounds,
                 v => _cfg.PlayFeedbackSounds = v);
        AddCheck(rows, "Keep the last recording as last.wav (for debugging)", _cfg.SaveLastWav,
                 v => _cfg.SaveLastWav = v);

        AddCombo(rows, "Microphone", MicrophoneChoices(), CurrentMicrophone(), v =>
            _cfg.InputDeviceContains = v == AnyMicrophone ? "" : v);

        AddNumber(rows, "Hold longer than this to dictate (ms)", _cfg.PttHoldMs, 50, 2000,
                  v => _cfg.PttHoldMs = (int)v);
        AddNumber(rows, "Double-tap window (ms)", _cfg.DoubleTapWindowMs, 150, 2000,
                  v => _cfg.DoubleTapWindowMs = (int)v);
        AddNumber(rows, "Ignore recordings shorter than (seconds)", (decimal)_cfg.MinRecordingSeconds, 0, 10,
                  v => _cfg.MinRecordingSeconds = (double)v, decimals: 2);
        AddNumber(rows, "Treat quieter than this as silence (% of full scale)", (decimal)_cfg.SilencePeakPercent, 0, 100,
                  v => _cfg.SilencePeakPercent = (double)v, decimals: 2);
        AddNumber(rows, "Entries kept in history (0 = off)", _cfg.HistorySize, 0, 1000,
                  v => _cfg.HistorySize = (int)v);

        AddNote(rows, "Hotkeys are assigned from the tray menu, where the app can watch you press them.");
        return page;
    }

    private TabPage BuildRecognition()
    {
        var page = NewPage("Recognition");
        var rows = NewRows(page);

        var groq = new RadioButton { Text = "Groq — Whisper in the cloud (needs an API key)", AutoSize = true };
        var local = new RadioButton { Text = "Parakeet v3 — on this PC, offline, no key", AutoSize = true };
        groq.Checked = !_cfg.UsesLocalEngine;
        local.Checked = _cfg.UsesLocalEngine;
        groq.CheckedChanged += (_, _) => { if (!_loading && groq.Checked) Apply(() => _cfg.SttEngine = "groq"); };
        local.CheckedChanged += (_, _) =>
        {
            if (_loading || !local.Checked) return;
            if (!ModelDownload.IsInstalled)
            {
                // Don't leave the setting pointing at an engine that cannot run:
                // ask first, and fall back to Groq if the download is declined.
                groq.Checked = true;
                ModelRequested?.Invoke();
                return;
            }
            Apply(() => _cfg.SttEngine = "parakeet");
        };
        rows.Controls.Add(groq);
        rows.Controls.Add(local);

        _modelStatus.AutoSize = true;
        _modelStatus.Margin = new Padding(20, 2, 0, 6);
        rows.Controls.Add(_modelStatus);

        _modelButton.Width = 200;
        _modelButton.Margin = new Padding(20, 0, 0, 10);
        _modelButton.Click += (_, _) => { ModelRequested?.Invoke(); RefreshModelStatus(); };
        rows.Controls.Add(_modelButton);

        AddCheck(rows, "If one engine fails, try the other", _cfg.SttFallback, v => _cfg.SttFallback = v);
        AddNumber(rows, "Unload the local model after idle (minutes, 0 = keep loaded)",
                  (decimal)_cfg.LocalUnloadAfterMinutes, 0, 1440,
                  v => _cfg.LocalUnloadAfterMinutes = (double)v);

        AddText(rows, "Groq transcription model", _cfg.TranscriptionModel, v => _cfg.TranscriptionModel = v);
        AddText(rows, "Language (empty = auto-detect)", _cfg.Language, v => _cfg.Language = v);

        AddNote(rows, "Auto-detect is what lets Russian and English mix inside one phrase — " +
                      "a fixed language only filters the on-device engine by script.");
        return page;
    }

    private TabPage BuildPaste()
    {
        var page = NewPage("Pasting");
        var rows = NewRows(page);

        AddCombo(rows, "How text is inserted",
                 new[] { "auto", "paste", "type" },
                 string.IsNullOrWhiteSpace(_cfg.PasteMode) ? "auto" : _cfg.PasteMode.ToLowerInvariant(),
                 v => _cfg.PasteMode = v);
        AddNote(rows, "auto — Ctrl+V, waiting for the clipboard when the window looks like a remote session; " +
                      "paste — always Ctrl+V; type — send the text as keystrokes.");

        AddCheck(rows, "Restore the previous clipboard after pasting", _cfg.RestoreClipboardAfterPaste,
                 v => _cfg.RestoreClipboardAfterPaste = v);
        AddNote(rows, "Off keeps the dictated text on the clipboard, so a paste that didn't arrive " +
                      "can be repeated by hand — which is what remote sessions need.");

        AddNumber(rows, "Head start for a remote client's clipboard (ms)", _cfg.RemotePasteDelayMs, 0, 5000,
                  v => _cfg.RemotePasteDelayMs = (int)v);
        AddCheck(rows, "Re-activate the remote window so it re-reads the clipboard", _cfg.RemoteClipboardFocusNudge,
                 v => _cfg.RemoteClipboardFocusNudge = v);
        AddNote(rows, "Off by default: taking the foreground away and giving it back is unreliable " +
                      "from a background process, and a stranded focus is worse than a stale paste.");

        AddText(rows, "Extra window titles that mean “remote session”",
                string.Join(", ", _cfg.RemoteWindowMarkers),
                v => _cfg.RemoteWindowMarkers = v
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return page;
    }

    private TabPage BuildLlm()
    {
        var page = NewPage("LLM & editing");
        var rows = NewRows(page);

        AddCheck(rows, "Edit selected text by voice", _cfg.EditSelection, v => _cfg.EditSelection = v);
        AddNote(rows, "Select text, hold the dictation key and say what to do with it " +
                      "(“сделай короче”, “переведи на английский”) — the result replaces the selection. " +
                      "Costs one Ctrl+C probe per dictation and needs an API key. Terminals are skipped, " +
                      "because there Ctrl+C interrupts the running command — but a terminal panel inside " +
                      "an editor looks like the editor, so turn this off if you dictate into one.");

        AddCheck(rows, "Tidy up dictation (punctuation, capitals, misheard words)", _cfg.CleanupTranscript,
                 v => _cfg.CleanupTranscript = v);
        AddNote(rows, "Only fixes how the words are written — it is told never to answer or obey what " +
                      "you said, and an answer that is not the same sentence is thrown away. Skipped " +
                      "silently when no model can be reached, so being offline just pastes what you said.");

        AddPassword(rows, "Groq API key", _cfg.GroqApiKey, v => _cfg.GroqApiKey = v.Trim());
        AddText(rows, "Chat models, best first (comma-separated)", string.Join(", ", _cfg.ChatModels),
                v =>
                {
                    var models = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (models.Length > 0) _cfg.ChatModels = models;
                });
        AddNote(rows, "A model that is rate limited or retired hands the job to the next one.");
        AddText(rows, "Task keywords (comma-separated)", string.Join(", ", _cfg.TaskKeywords),
                v =>
                {
                    var words = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (words.Length > 0) _cfg.TaskKeywords = words;
                });
        AddNumber(rows, "Look for a task keyword within the first N words", _cfg.TaskKeywordMaxWordPosition, 1, 20,
                  v => _cfg.TaskKeywordMaxWordPosition = (int)v);

        AddMultiline(rows, "System prompt for task mode (empty = built-in)", _cfg.TaskSystemPrompt,
                     v => _cfg.TaskSystemPrompt = v);
        return page;
    }

    // ---------------- model status ----------------

    public void RefreshModelStatus()
    {
        bool installed = ModelDownload.IsInstalled;
        _modelStatus.Text = installed
            ? $"Model installed in {ModelDownload.ModelDir}"
            : $"Model not downloaded — about {ModelDownload.DownloadMB} MB.";
        _modelButton.Text = installed ? "Delete downloaded model…" : "Download model…";
    }

    // ---------------- building blocks ----------------

    private const string AnyMicrophone = "(system default)";

    private static string[] MicrophoneChoices() =>
        new[] { AnyMicrophone }.Concat(Recorder.ListDeviceNames()).ToArray();

    private string CurrentMicrophone() =>
        string.IsNullOrWhiteSpace(_cfg.InputDeviceContains)
            ? AnyMicrophone
            : Recorder.ListDeviceNames().FirstOrDefault(
                  n => n.Contains(_cfg.InputDeviceContains, StringComparison.OrdinalIgnoreCase))
              ?? _cfg.InputDeviceContains;

    private static TabPage NewPage(string title) => new(title) { UseVisualStyleBackColor = true };

    private static FlowLayoutPanel NewRows(TabPage page)
    {
        var rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(14, 12, 14, 12),
        };
        page.Controls.Add(rows);
        return rows;
    }

    /// <summary>Runs a setter, persists, and tells the app to re-apply itself.</summary>
    private void Apply(Action set)
    {
        if (_loading) return;
        try
        {
            set();
            _cfg.Save();
            Changed?.Invoke();
        }
        catch (Exception ex) { Log.Error("settings change failed", ex); }
    }

    private void AddCheck(Control parent, string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        c.CheckedChanged += (_, _) => Apply(() => set(c.Checked));
        parent.Controls.Add(c);
    }

    private void AddText(Control parent, string label, string value, Action<string> set)
    {
        parent.Controls.Add(NewLabel(label));
        var box = new TextBox { Text = value, Width = 520, Margin = new Padding(0, 0, 0, 8) };
        box.TextChanged += (_, _) => Apply(() => set(box.Text));
        parent.Controls.Add(box);
    }

    private void AddPassword(Control parent, string label, string value, Action<string> set)
    {
        parent.Controls.Add(NewLabel(label));
        var box = new TextBox { Text = value, Width = 520, UseSystemPasswordChar = true };
        box.TextChanged += (_, _) => Apply(() => set(box.Text));
        var show = new CheckBox { Text = "Show", AutoSize = true, Margin = new Padding(0, 2, 0, 8) };
        show.CheckedChanged += (_, _) => box.UseSystemPasswordChar = !show.Checked;
        parent.Controls.Add(box);
        parent.Controls.Add(show);
    }

    private void AddMultiline(Control parent, string label, string value, Action<string> set)
    {
        parent.Controls.Add(NewLabel(label));
        var box = new TextBox
        {
            Text = value, Width = 520, Height = 90, Multiline = true,
            ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 0, 0, 8),
        };
        box.TextChanged += (_, _) => Apply(() => set(box.Text));
        parent.Controls.Add(box);
    }

    private void AddNumber(Control parent, string label, decimal value, decimal min, decimal max,
                           Action<decimal> set, int decimals = 0)
    {
        parent.Controls.Add(NewLabel(label));
        var box = new NumericUpDown
        {
            Minimum = min, Maximum = max, DecimalPlaces = decimals, Width = 120,
            Increment = decimals > 0 ? 0.1m : 1m,
            Value = Math.Clamp(value, min, max),
            Margin = new Padding(0, 0, 0, 8),
        };
        box.ValueChanged += (_, _) => Apply(() => set(box.Value));
        parent.Controls.Add(box);
    }

    private void AddCombo(Control parent, string label, string[] items, string value, Action<string> set)
    {
        parent.Controls.Add(NewLabel(label));
        var box = new ComboBox { Width = 520, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 8) };
        box.Items.AddRange(items);
        box.SelectedItem = items.Contains(value) ? value : items.FirstOrDefault();
        box.SelectedIndexChanged += (_, _) => Apply(() => set((string)box.SelectedItem!));
        parent.Controls.Add(box);
    }

    private static Label NewLabel(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };

    private static void AddNote(Control parent, string text)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            MaximumSize = new Size(530, 0),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 10),
        });
    }

    private static void OpenInEditor(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"could not open {path}: {ex.Message}"); }
    }
}
