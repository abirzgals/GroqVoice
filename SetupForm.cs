using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// First-run / change-key dialog. Shown automatically when Config.GroqApiKey is empty;
/// also reachable from the tray menu via "Setup…".
/// </summary>
public sealed class SetupForm : Form
{
    private const string GroqKeysUrl = "https://console.groq.com/keys";

    private readonly Config _cfg;
    private readonly TextBox _txtKey;
    private readonly CheckBox _chkShow;
    private readonly Button _btnSave;
    private readonly Label _lblStatus;

    public bool Saved { get; private set; }

    public SetupForm(Config cfg)
    {
        _cfg = cfg;

        Text = "GroqVoice — Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(560, 460);
        Font = new Font("Segoe UI", 9.5f);
        TopMost = true;

        // ---------- header ----------
        var lblTitle = new Label
        {
            Text = "Welcome to GroqVoice",
            Location = new Point(20, 16),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
        };
        Controls.Add(lblTitle);

        // ---------- description ----------
        var lblDesc = new Label
        {
            Location = new Point(20, 56),
            Size = new Size(520, 110),
            Text =
                "Hold Win+Ctrl, speak, release — your speech is transcribed via Groq Whisper " +
                "and pasted into the focused window.\r\n\r\n" +
                "Start your phrase with \"task\" or \"задача\" to route through Llama 3.3 70B " +
                "instead — useful for translations, code snippets, ASCII art, quick answers.\r\n\r\n" +
                "Russian + English mixed dictation works out of the box.",
        };
        Controls.Add(lblDesc);

        // ---------- how to get a key ----------
        var lblHowto = new Label
        {
            Location = new Point(20, 180),
            Size = new Size(520, 70),
            Text =
                "You'll need a free Groq API key:\r\n" +
                "  1.  Open the Groq console (link below) and sign up\r\n" +
                "  2.  Create an API key — name it anything\r\n" +
                "  3.  Paste the key here and click Save",
        };
        Controls.Add(lblHowto);

        var linkConsole = new LinkLabel
        {
            Text = "Open Groq Console — " + GroqKeysUrl,
            Location = new Point(20, 256),
            AutoSize = true,
        };
        linkConsole.LinkClicked += (_, _) => OpenUrl(GroqKeysUrl);
        Controls.Add(linkConsole);

        // ---------- key input ----------
        var lblKey = new Label
        {
            Text = "Groq API key:",
            Location = new Point(20, 296),
            AutoSize = true,
        };
        Controls.Add(lblKey);

        _txtKey = new TextBox
        {
            Location = new Point(20, 318),
            Size = new Size(520, 26),
            Font = new Font("Consolas", 10f),
            UseSystemPasswordChar = true,
            Text = _cfg.GroqApiKey ?? "",
            PlaceholderText = "gsk_…",
        };
        _txtKey.TextChanged += (_, _) => UpdateUi();
        Controls.Add(_txtKey);

        _chkShow = new CheckBox
        {
            Text = "Show key",
            Location = new Point(20, 348),
            AutoSize = true,
        };
        _chkShow.CheckedChanged += (_, _) => _txtKey.UseSystemPasswordChar = !_chkShow.Checked;
        Controls.Add(_chkShow);

        _lblStatus = new Label
        {
            Location = new Point(20, 376),
            Size = new Size(520, 20),
            ForeColor = Color.DarkRed,
            Text = "",
        };
        Controls.Add(_lblStatus);

        // ---------- buttons ----------
        _btnSave = new Button
        {
            Text = "Save",
            Location = new Point(360, 410),
            Size = new Size(85, 32),
        };
        _btnSave.Click += OnSaveClicked;
        Controls.Add(_btnSave);

        var btnQuit = new Button
        {
            Text = string.IsNullOrWhiteSpace(_cfg.GroqApiKey) ? "Quit" : "Cancel",
            Location = new Point(455, 410),
            Size = new Size(85, 32),
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(btnQuit);

        AcceptButton = _btnSave;
        CancelButton = btnQuit;

        UpdateUi();
    }

    private void UpdateUi()
    {
        var k = (_txtKey.Text ?? "").Trim();
        bool looksOk = k.StartsWith("gsk_", StringComparison.Ordinal) && k.Length >= 20;
        _btnSave.Enabled = looksOk;
        _lblStatus.Text = k.Length == 0
            ? ""
            : looksOk ? "" : "Groq keys start with \"gsk_\" — double-check the value you pasted.";
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var key = (_txtKey.Text ?? "").Trim();
        if (!key.StartsWith("gsk_", StringComparison.Ordinal) || key.Length < 20) return;

        _cfg.GroqApiKey = key;
        try
        {
            _cfg.Save();
            Log.Info("API key saved via SetupForm");
            Saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("could not save config", ex);
            _lblStatus.Text = "Could not save config: " + ex.Message;
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"open url failed: {ex.Message}"); }
    }
}
