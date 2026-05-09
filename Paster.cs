using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// Stuffs text into the clipboard, sends Ctrl+V to the focused window,
/// then restores the previous clipboard contents.
/// All clipboard work runs on a hidden STA thread (WinForms requirement).
/// </summary>
public static class Paster
{
    public static void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        string? prev = null;
        var t = new Thread(() =>
        {
            try { if (Clipboard.ContainsText()) prev = Clipboard.GetText(); } catch { }
            try { Clipboard.SetText(text); } catch { }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        SendCtrlV();

        // give the foreground app ~250 ms to consume the paste before we restore the clipboard
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
}
