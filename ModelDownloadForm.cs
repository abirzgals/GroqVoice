namespace GroqVoice;

/// <summary>
/// Asks before spending 460 MB, then shows the download filling up. Modeless and
/// always-on-top: dictation keeps working (through Groq) while the model arrives,
/// and closing the window cancels nothing — only Cancel does.
/// </summary>
public sealed class ModelDownloadForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _status;
    private readonly Button _cancel;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Raised on the UI thread when the model is installed and ready.</summary>
    public event Action? Installed;

    private ModelDownloadForm()
    {
        Text = "GroqVoice — local model";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 120);

        _status = new Label
        {
            Left = 14, Top = 16, Width = 392, Height = 34,
            Text = $"Downloading Parakeet v3 (~{ModelDownload.DownloadMB} MB)…",
        };
        _bar = new ProgressBar { Left = 14, Top = 54, Width = 392, Height = 20, Minimum = 0, Maximum = 1000 };
        _cancel = new Button { Left = 326, Top = 84, Width = 80, Text = "Cancel" };
        _cancel.Click += (_, _) => { _cts.Cancel(); _status.Text = "Cancelling…"; _cancel.Enabled = false; };

        Controls.AddRange(new Control[] { _status, _bar, _cancel });
        FormClosed += (_, _) => _cts.Dispose();
    }

    /// <summary>
    /// Confirms with the user and, on yes, starts the download in a window.
    /// Returns false if the user declined or the model is already there.
    /// </summary>
    public static bool Prompt(IWin32Window? owner, Action? onInstalled)
    {
        if (ModelDownload.IsInstalled) { onInstalled?.Invoke(); return true; }

        var answer = MessageBox.Show(owner,
            $"Recognize speech on this PC with Parakeet v3?\n\n" +
            $"Works offline and needs no API key — Russian, English, Latvian and 22 more " +
            $"languages, mixed within one phrase.\n\n" +
            $"One-time download: about {ModelDownload.DownloadMB} MB (~660 MB on disk).\n" +
            $"It is stored in %APPDATA%\\GroqVoice\\models and kept across updates.\n\n" +
            $"Download it now?",
            "GroqVoice — download local model",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return false;

        var form = new ModelDownloadForm();
        if (onInstalled != null) form.Installed += onInstalled;
        form.Show();
        form.Start();
        return true;
    }

    private void Start()
    {
        var ui = SynchronizationContext.Current!;
        var progress = new Progress<ModelDownload.Progress>(p => Apply(p));

        _ = Task.Run(async () =>
        {
            try
            {
                await ModelDownload.InstallAsync(progress, _cts.Token).ConfigureAwait(false);
                ui.Post(_ =>
                {
                    Installed?.Invoke();
                    Close();
                }, null);
            }
            catch (OperationCanceledException)
            {
                Log.Info("model download cancelled");
                ui.Post(_ => Close(), null);
            }
            catch (Exception ex)
            {
                Log.Error("model download failed", ex);
                ui.Post(_ =>
                {
                    MessageBox.Show(this, ex.Message, "GroqVoice — download failed",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }, null);
            }
        });
    }

    private void Apply(ModelDownload.Progress p)
    {
        if (IsDisposed) return;
        if (p.Extracting)
        {
            _status.Text = "Unpacking the model…";
            _bar.Style = ProgressBarStyle.Marquee;
            _cancel.Enabled = false;
            return;
        }
        _bar.Value = (int)Math.Clamp(p.Fraction * 1000, 0, 1000);
        double doneMB = p.BytesDone / 1048576.0, totalMB = p.BytesTotal / 1048576.0;
        _status.Text = $"Downloading Parakeet v3 — {p.Fraction * 100:0}%  ({doneMB:0} / {totalMB:0} MB)";
        Text = $"GroqVoice — local model {p.Fraction * 100:0}%";
    }
}
