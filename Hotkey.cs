using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GroqVoice;

/// <summary>
/// Low-level keyboard hook detecting "hold both Win and Ctrl" as a chord.
/// While the chord is held with no other key, raises ChordPressed once and
/// ChordReleased when either side is released. Events bubble through normally
/// — we never swallow keys, so OS shortcuts (Win+Ctrl+D, etc.) still work.
/// </summary>
public sealed class Hotkey : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    private bool _winHeld;
    private bool _ctrlHeld;
    private bool _chordActive;
    private bool _otherKeyDuringChord;

    public event Action? ChordPressed;
    /// <summary>Fires when either modifier is released. clean=false means a third key was pressed
    /// during the chord (e.g. Win+Ctrl+D) — caller should discard, not act.</summary>
    public event Action<bool>? ChordReleased;

    public Hotkey()
    {
        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("Failed to install keyboard hook.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var msg = wParam.ToInt32();
        bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
        uint vk = kbd.vkCode;

        bool isWin = vk == VK_LWIN || vk == VK_RWIN;
        bool isCtrl = vk == VK_LCONTROL || vk == VK_RCONTROL;

        if (isDown)
        {
            if (isWin) _winHeld = true;
            else if (isCtrl) _ctrlHeld = true;
            else if (_chordActive) _otherKeyDuringChord = true;

            // Chord fires the instant both modifiers are held — even if the second-down
            // is the modifier itself. Auto-repeats just re-enter this branch with _chordActive
            // already true, so ChordPressed only fires once.
            if (!_chordActive && _winHeld && _ctrlHeld)
            {
                _chordActive = true;
                _otherKeyDuringChord = false;
                try { ChordPressed?.Invoke(); } catch { /* never let user code break the hook */ }
            }
        }
        else if (isUp)
        {
            if (isWin) _winHeld = false;
            else if (isCtrl) _ctrlHeld = false;

            if (_chordActive && (!_winHeld || !_ctrlHeld))
            {
                _chordActive = false;
                bool clean = !_otherKeyDuringChord;
                _otherKeyDuringChord = false;
                try { ChordReleased?.Invoke(clean); } catch { }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
