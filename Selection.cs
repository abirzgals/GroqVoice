using System.Runtime.InteropServices;
using System.Text.Json;

namespace GroqVoice;

/// <summary>
/// Reads whatever is selected in the focused application by sending Ctrl+C and
/// watching the clipboard. Windows has no general way to ask an arbitrary window
/// for its selection — UI Automation covers native controls but not Electron,
/// browsers or remote sessions — and a copy is the one gesture every text surface
/// understands.
///
/// The clipboard is put back exactly as it was, so the probe leaves no trace
/// beyond the copy itself.
/// </summary>
public static class Selection
{
    /// <summary>How long to wait for the app to answer the copy.</summary>
    private const int ProbeTimeoutMs = 220;

    /// <summary>
    /// Returns the selected text, or null when nothing is selected (or the app
    /// does not answer Ctrl+C within the timeout).
    /// </summary>
    public static string? TryRead()
    {
        try
        {
            // In a terminal Ctrl+C is not "copy" — it interrupts whatever is
            // running. Dictating into one must never cost the user a build.
            if (ForegroundApp.IsConsole(out var why))
            {
                Log.Info($"selection: skipping the Ctrl+C probe in a console ({why})");
                return null;
            }

            uint before = GetClipboardSequenceNumber();
            string? saved = Paster.ReadClipboardText();

            Paster.SendCtrlC();

            var deadline = Environment.TickCount64 + ProbeTimeoutMs;
            while (GetClipboardSequenceNumber() == before && Environment.TickCount64 < deadline)
                Thread.Sleep(10);

            if (GetClipboardSequenceNumber() == before)
            {
                Log.Info("selection: nothing copied — treating as plain dictation");
                return null;
            }

            string? copied = Paster.ReadClipboardText();
            bool wholeLine = IsVsCodeEmptySelectionCopy();

            // Put the user's clipboard back before deciding anything.
            if (saved != copied) Paster.WriteClipboardText(saved);

            if (wholeLine)
            {
                Log.Info("selection: VS Code copied the whole line (nothing selected) — ignoring");
                return null;
            }
            if (string.IsNullOrWhiteSpace(copied)) return null;

            Log.Info($"selection: {copied.Length} chars from {ForegroundApp.Describe()}");
            return copied;
        }
        catch (Exception ex)
        {
            Log.Warn($"selection probe failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// VS Code and other Monaco editors copy the entire current line when nothing
    /// is selected — which would look like a selection to us and silently rewrite
    /// a line the user never picked. They flag it on their own clipboard format.
    /// </summary>
    private static bool IsVsCodeEmptySelectionCopy()
    {
        try
        {
            uint fmt = RegisterClipboardFormat("vscode-editor-data");
            if (fmt == 0 || !IsClipboardFormatAvailable(fmt)) return false;

            string? json = ReadClipboardFormatAsString(fmt);
            if (json is null) return false;

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("isFromEmptySelection", out var flag) &&
                   flag.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
    }

    /// <summary>Reads one registered clipboard format as UTF-8 text.</summary>
    private static string? ReadClipboardFormatAsString(uint format)
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr h = GetClipboardData(format);
            if (h == IntPtr.Zero) return null;

            IntPtr p = GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try
            {
                int size = (int)GlobalSize(h);
                if (size <= 0) return null;
                var bytes = new byte[size];
                Marshal.Copy(p, bytes, 0, size);
                // The payload is NUL-terminated UTF-8.
                int end = Array.IndexOf(bytes, (byte)0);
                return System.Text.Encoding.UTF8.GetString(bytes, 0, end < 0 ? size : end);
            }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr hMem);
}
