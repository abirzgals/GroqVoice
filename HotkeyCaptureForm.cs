using System.Drawing;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// "Press the combination you want" dialog. Keys are read through the app's own
/// low-level hook (put into capture mode) rather than via WinForms KeyDown —
/// the Win key never reaches a managed form, and capture mode also swallows the
/// keystrokes so the Start menu doesn't pop open mid-assignment.
///
/// The combo shown is the peak of what was held: it grows while keys go down and
/// is left alone as they come back up, so releasing the chord doesn't erase it.
/// </summary>
public sealed class HotkeyCaptureForm : Form
{
    private readonly Hotkey _hotkey;
    private readonly Label _preview;
    private readonly Label _hint;
    private readonly Button _ok;
    private HotkeyCombo _captured;

    public HotkeyCombo Result => _captured;

    public HotkeyCaptureForm(Hotkey hotkey, string actionName, HotkeyCombo current)
    {
        _hotkey = hotkey;
        _captured = current;

        Text = $"Assign hotkey — {actionName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 190);

        var caption = new Label
        {
            Text = $"Press the key combination for {actionName}, then click OK.",
            Location = new Point(16, 16),
            Size = new Size(388, 20),
        };

        _preview = new Label
        {
            Text = current.ToString(),
            Location = new Point(16, 46),
            Size = new Size(388, 44),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
        };

        _hint = new Label
        {
            Location = new Point(16, 96),
            Size = new Size(388, 34),
            ForeColor = SystemColors.GrayText,
            Text = "Hold any number of keys — mouse buttons are ignored.\r\n"
                 + "Enter or OK saves.  Esc or closing the window discards.",
        };

        _ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(228, 144),
            Size = new Size(84, 28),
            Enabled = current.IsUsable,
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(320, 144),
            Size = new Size(84, 28),
        };

        Controls.AddRange(new Control[] { caption, _preview, _hint, _ok, cancel });
        AcceptButton = _ok;
        CancelButton = cancel;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _hotkey.CaptureUpdated += OnCaptureUpdated;
        _hotkey.CaptureMode = true;
    }

    private void OnCaptureUpdated(HotkeyCombo seen)
    {
        // Marshal to the UI thread: the hook fires on whichever thread installed it.
        if (InvokeRequired) { try { BeginInvoke(() => OnCaptureUpdated(seen)); } catch { } return; }

        // Whatever was held is the combo — any number of keys, and no opinion here
        // about which of them are sensible.
        _captured = seen;
        _preview.Text = seen.ToString();
        _ok.Enabled = seen.IsUsable;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hotkey.CaptureMode = false;
        _hotkey.CaptureUpdated -= OnCaptureUpdated;
        base.OnFormClosed(e);
    }
}
