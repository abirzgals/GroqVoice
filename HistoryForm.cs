namespace GroqVoice;

/// <summary>
/// The last results, so a paste that went into the wrong window — or a rewrite
/// that came out wrong — is never lost. Pasting from here goes into whatever was
/// focused before this window opened, which is the whole point, so the window
/// hands the focus back before sending Ctrl+V.
/// </summary>
public sealed class HistoryForm : Form
{
    private readonly History _history;
    private readonly Func<PasteOptions> _pasteOptions;
    private readonly bool _restoreClipboard;

    private readonly ListBox _list = new();
    private readonly TextBox _detail = new();
    private readonly Label _meta = new();
    private readonly Button _paste = new();
    private readonly Button _copy = new();
    private readonly Button _original = new();

    private HistoryEntry[] _shown = Array.Empty<HistoryEntry>();
    private IntPtr _previousWindow;

    public HistoryForm(History history, Func<PasteOptions> pasteOptions, bool restoreClipboard)
    {
        _history = history;
        _pasteOptions = pasteOptions;
        _restoreClipboard = restoreClipboard;

        Text = "GroqVoice — History";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 460);
        MinimumSize = new Size(560, 360);
        // Taking the foreground would defeat the point: pasting must land back in
        // the window the user came from.
        ShowInTaskbar = true;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            // The split is only honoured once the container has its real size, so
            // it is set again in OnLoad; here it just keeps the designer happy.
            FixedPanel = FixedPanel.None,
        };
        Load += (_, _) =>
        {
            try { split.SplitterDistance = (int)(split.Width * 0.45); } catch { }
        };

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.DoubleClick += (_, _) => PasteSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) DeleteSelected();
            else if (e.KeyCode == Keys.Enter) PasteSelected();
            else if (e.KeyCode == Keys.Escape) Close();
        };
        split.Panel1.Controls.Add(_list);

        _meta.Dock = DockStyle.Top;
        _meta.Height = 22;
        _meta.TextAlign = ContentAlignment.MiddleLeft;

        _detail.Dock = DockStyle.Fill;
        _detail.Multiline = true;
        _detail.ReadOnly = true;
        _detail.ScrollBars = ScrollBars.Vertical;
        _detail.BackColor = SystemColors.Window;

        split.Panel2.Controls.Add(_detail);
        split.Panel2.Controls.Add(_meta);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 42,
            Padding = new Padding(8, 6, 8, 6),
        };
        _paste.Text = "Paste";
        _paste.Width = 90;
        _paste.Click += (_, _) => PasteSelected();
        _copy.Text = "Copy";
        _copy.Width = 90;
        _copy.Click += (_, _) => CopySelected();
        _original.Text = "Show original";
        _original.Width = 120;
        _original.Click += (_, _) => ShowOriginal();
        var delete = new Button { Text = "Delete", Width = 90 };
        delete.Click += (_, _) => DeleteSelected();
        var clear = new Button { Text = "Clear all…", Width = 100 };
        clear.Click += (_, _) => ClearAll();
        buttons.Controls.AddRange(new Control[] { _paste, _copy, _original, delete, clear });

        Controls.Add(split);
        Controls.Add(buttons);

        _history.Changed += OnHistoryChanged;
        FormClosed += (_, _) => _history.Changed -= OnHistoryChanged;

        Reload();
    }

    /// <summary>Remembered before the window is shown, so Paste can hand focus back.</summary>
    public void RememberForegroundWindow(IntPtr hwnd) => _previousWindow = hwnd;

    private void OnHistoryChanged()
    {
        if (IsDisposed) return;
        try { BeginInvoke(new Action(Reload)); } catch { }
    }

    private void Reload()
    {
        // Newest first: the entry you want is almost always the last one.
        _shown = _history.Entries.Reverse().ToArray();
        int keep = _list.SelectedIndex;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _shown)
        {
            string tag = e.Kind == nameof(TakeKind.Dictation) ? "" : $"[{e.Kind.ToLowerInvariant()}] ";
            _list.Items.Add($"{e.Time:HH:mm}  {tag}{e.OneLine(52)}");
        }
        _list.EndUpdate();

        if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Clamp(keep, 0, _list.Items.Count - 1);
        else
        {
            _detail.Text = "";
            _meta.Text = "Nothing dictated yet.";
        }
        UpdateButtons();
    }

    private HistoryEntry? Current =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Length ? _shown[_list.SelectedIndex] : null;

    private void ShowSelected()
    {
        var e = Current;
        if (e == null) return;
        _detail.Text = e.Text.Replace("\n", "\r\n");
        _meta.Text = $"{e.Time:dd.MM.yyyy HH:mm:ss}  ·  {e.Kind.ToLowerInvariant()}  ·  {e.Text.Length} chars";
        UpdateButtons();
    }

    private void ShowOriginal()
    {
        var e = Current;
        if (e?.Source == null) return;
        _detail.Text = e.Source.Replace("\n", "\r\n");
        _meta.Text = $"{e.Time:dd.MM.yyyy HH:mm:ss}  ·  what went into the model  ·  {e.Source.Length} chars";
    }

    private void UpdateButtons()
    {
        var e = Current;
        _paste.Enabled = _copy.Enabled = e != null;
        _original.Enabled = e?.Source != null;
    }

    private void PasteSelected()
    {
        var e = Current;
        if (e == null) return;
        Paster.PasteInto(_previousWindow, e.Text, _restoreClipboard, _pasteOptions());
    }

    private void CopySelected()
    {
        var e = Current;
        if (e == null) return;
        Paster.WriteClipboardText(e.Text);
        _meta.Text = "Copied to the clipboard.";
    }

    private void DeleteSelected()
    {
        var e = Current;
        if (e == null) return;
        _history.Remove(e);
    }

    private void ClearAll()
    {
        if (_history.Entries.Count == 0) return;
        var answer = MessageBox.Show(this,
            $"Delete all {_history.Entries.Count} entries from history?",
            "GroqVoice — clear history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes) _history.Clear();
    }
}
