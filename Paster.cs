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
    public int RemoteDelayMs { get; init; } = 800;
    /// <summary>
    /// Re-activate the remote window first, so the client re-reads the clipboard.
    /// Off by default: forcing activation from a background process runs into the
    /// foreground lock and was measured to be unreliable in both directions.
    /// </summary>
    public bool NudgeFocus { get; init; } = false;
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
            Log.Info($"paste: remote session detected ({why})");

            // These clients read the local clipboard when their window is activated,
            // not when the clipboard changes. Dictating without ever leaving the
            // session produces no activation, so the far machine keeps pasting whatever
            // was there when the window was last entered — no amount of waiting helps.
            if (opt.NudgeFocus) NudgeActivation();

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

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);

    /// <summary>
    /// Deactivates and re-activates the foreground window so a remote-desktop client
    /// re-reads the local clipboard.
    ///
    /// A Win32 SetFocus cycle was tried first and did nothing: Chrome drives the page's
    /// focus from window *activation*, so a focus change on the top-level HWND never
    /// reaches the renderer. Activation is what happens when the user alt-tabs away and
    /// back — the one thing that demonstrably syncs the clipboard.
    ///
    /// The taskbar stands in as the intermediate window: it always exists and
    /// activating it shows the user nothing. AttachThreadInput lifts the foreground
    /// lock that would otherwise make SetForegroundWindow fail from a background
    /// process. The restore is verified and retried, because leaving the user parked on
    /// the taskbar would be a great deal worse than a stale paste.
    /// </summary>
    private static void NudgeActivation()
    {
        var target = GetForegroundWindow();
        if (target == IntPtr.Zero) { Log.Warn("paste: no foreground window to re-activate"); return; }

        var standIn = FindWindow("Shell_TrayWnd", null);
        if (standIn == IntPtr.Zero) { Log.Warn("paste: taskbar window not found, skipping re-activation"); return; }

        if (!Activate(standIn)) { Log.Warn("paste: could not deactivate the window, clipboard not refreshed"); return; }

        if (Activate(target))
            Log.Info("paste: window re-activated so the client re-reads the clipboard");
        else
            Log.Error("paste: FOREGROUND NOT RESTORED after re-activation - focus may be on the taskbar");
    }

    /// <summary>
    /// Attaches to whichever thread owns the foreground at this moment before asking for
    /// it. Attaching once up front does not work: after the first switch the foreground
    /// belongs to a different thread, and the call is refused.
    /// </summary>
    private static bool Activate(IntPtr want)
    {
        uint us = GetCurrentThreadId();
        for (int i = 0; i < 6; i++)
        {
            var fg = GetForegroundWindow();
            if (fg == want) return true;

            uint owner = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, IntPtr.Zero);
            bool attached = owner != 0 && owner != us && AttachThreadInput(us, owner, true);
            try { SetForegroundWindow(want); }
            finally { if (attached) AttachThreadInput(us, owner, false); }

            Thread.Sleep(50);
        }
        return GetForegroundWindow() == want;
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        uint sent = 0;
        foreach (var ch in text)
        {
            var inputs = new[] { Char(ch, false), Char(ch, true) };
            sent += SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
        sw.Stop();

        uint expected = (uint)text.Length * 2;
        if (sent != expected)
            Log.Warn($"paste: typing accepted {sent}/{expected} events after {sw.ElapsedMilliseconds} ms — " +
                     $"last error {Marshal.GetLastWin32Error()}");
        else
            Log.Info($"paste: typed {text.Length} chars in {sw.ElapsedMilliseconds} ms");
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

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_VSC = 0;

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
            ki = new KEYBDINPUT
            {
                wVk = vk,
                // Browsers derive KeyboardEvent.code from the hardware scan code, and a
                // remote-desktop client forwards keys by that code. Left at 0 these
                // arrive with an empty code, so Chrome Remote Desktop had nothing to
                // forward and the Ctrl+V never reached the far machine at all — the
                // clipboard was never the problem for the keystroke.
                wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
            }
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
