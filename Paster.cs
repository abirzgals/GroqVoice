using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// Stuffs text into the clipboard, sends Ctrl+V to the focused window,
/// then restores the previous clipboard contents.
/// All clipboard work runs on a hidden STA thread (WinForms requirement).
/// </summary>
public enum PasteMode { Auto, Paste, Type }

public sealed class PasteOptions
{
    public PasteMode Mode { get; init; } = PasteMode.Auto;
    /// <summary>Head start given to the client's clipboard sync before Ctrl+V.</summary>
    public int RemoteDelayMs { get; init; } = 400;
    public string[]? RemoteWindowMarkers { get; init; }
}

public static class Paster
{
    public static void Paste(string text, bool restoreClipboard = false, PasteOptions? options = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var opt = options ?? new PasteOptions();

        bool remote = false;
        string why = "";
        if (opt.Mode != PasteMode.Paste)
            remote = ForegroundApp.IsRemoteSession(opt.RemoteWindowMarkers, out why);

        bool type = opt.Mode == PasteMode.Type;

        if (type)
        {
            Log.Info($"paste: typing {text.Length} chars into {ForegroundApp.Describe()} (mode=type)");
            // Still leave it on the clipboard so a failed run can be pasted by hand.
            SetClipboard(text, restoreClipboard, out _);
            TypeUnicode(text);
            return;
        }

        string? prev = null;
        SetClipboard(text, restoreClipboard, out prev);

        if (remote)
        {
            // The far machine pastes from its own clipboard, so give the client's
            // sync a moment to carry ours across before the keystroke lands.
            Log.Info($"paste: remote session detected ({why}) — waiting {opt.RemoteDelayMs} ms for clipboard sync");
            Thread.Sleep(Math.Max(0, opt.RemoteDelayMs));
        }
        else
        {
            Log.Info($"paste: Ctrl+V into {ForegroundApp.Describe()}");
        }

        SendCtrlV();

        if (!restoreClipboard) return;

        // optional legacy behaviour: hand the foreground app ~250 ms to consume the
        // paste, then restore whatever the user had on the clipboard beforehand.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(250);
            var r = new Thread(() =>
            {
                try
                {
                    if (prev is null) Clipboard.Clear();
                    else Clipboard.SetText(prev);
                }
                catch { }
            });
            r.SetApartmentState(ApartmentState.STA);
            r.Start();
            r.Join();
        });
    }

    private static void SetClipboard(string text, bool keepPrevious, out string? previous)
    {
        string? prev = null;
        var t = new Thread(() =>
        {
            try { if (keepPrevious && Clipboard.ContainsText()) prev = Clipboard.GetText(); } catch { }
            try { Clipboard.SetText(text); } catch (Exception ex) { Log.Warn($"clipboard set failed: {ex.Message}"); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        previous = prev;
    }

    /// <summary>
    /// Sends the text as literal characters rather than a paste. Uses KEYEVENTF_UNICODE
    /// so it is independent of the active keyboard layout — which matters for dictated
    /// Cyrillic, since mapping characters onto virtual keys would need the far machine
    /// to have a matching layout loaded.
    /// </summary>
    private static void TypeUnicode(string text)
    {
        for (int i = 0; i < 30 && (IsDown(VK_LWIN) || IsDown(VK_RWIN) || IsDown(VK_CONTROL)); i++)
            Thread.Sleep(10);

        // One batch per character keeps ordering deterministic across remote clients,
        // which can reorder or coalesce a single large batch.
        foreach (var ch in text)
        {
            var inputs = new[] { Char(ch, false), Char(ch, true) };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static void SendCtrlV()
    {
        // If the user is still holding Win/Ctrl when we paste, those would combine with V
        // to produce Win+Ctrl+V — not what we want. Wait briefly for them to release.
        for (int i = 0; i < 30 && (IsDown(VK_LWIN) || IsDown(VK_RWIN)); i++)
            Thread.Sleep(10);

        var inputs = new INPUT[]
        {
            Key(VK_CONTROL, false),
            Key(VK_V,       false),
            Key(VK_V,       true),
            Key(VK_CONTROL, true),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static bool IsDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static INPUT Key(ushort vk, bool up) => new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 }
        }
    };

    private static INPUT Char(char ch, bool up) => new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
            }
        }
    };
}
