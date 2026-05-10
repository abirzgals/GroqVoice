using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GroqVoice;

/// <summary>
/// Detects "Win+Ctrl held together" as a chord and exposes two modes:
///
///   • Press &amp; hold  →  push-to-talk: ChordPressed fires immediately,
///                       ChordReleased fires when either modifier is released.
///
///   • Quick double-tap (press-release-press within <see cref="DoubleTapWindowMs"/> ms)
///                    →  toggle: recording starts and stays on regardless of key state.
///                       The next single press fires ChordReleased to stop it.
///
/// "Quick" means the chord was held for less than <see cref="PttHoldMs"/> ms.
/// Anything longer is treated as a normal PTT release and fires ChordReleased
/// immediately. Keys are never swallowed, so OS shortcuts (Win+Ctrl+D etc.) work
/// — when a third key is detected during the chord the release is reported as
/// dirty and the caller discards the audio.
/// </summary>
public sealed class Hotkey : IDisposable
{
    public int PttHoldMs { get; set; } = 250;
    public int DoubleTapWindowMs { get; set; } = 400;

    public event Action? ChordPressed;
    /// <summary>clean=false means a third key was pressed during the chord.</summary>
    public event Action<bool>? ChordReleased;
    /// <summary>Fires once when Win+Ctrl+Alt are all simultaneously held. Cancels any in-progress voice recording.</summary>
    public event Action? ScreenshotTriggered;

    // ---- low-level hook plumbing ----
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

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

    // chord-detection (low-level)
    private bool _winHeld;
    private bool _ctrlHeld;
    private bool _altHeld;
    private bool _chordActive;
    private bool _otherKeyDuringChord;
    private bool _screenshotFired;   // gate so we only fire once per Win+Ctrl+Alt session

    // state machine (high-level: PTT vs toggle)
    private enum Mode { Idle, Toggle }
    private readonly object _smLock = new();
    private Mode _mode = Mode.Idle;
    private bool _recordingActive;
    private DateTime _pressTimeUtc;
    private bool _waitingForSecondTap;
    private bool _firstTapClean;
    private System.Threading.Timer? _doubleTapTimer;

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
        bool isAlt = vk == VK_LMENU || vk == VK_RMENU;

        if (isDown)
        {
            if (isWin) _winHeld = true;
            else if (isCtrl) _ctrlHeld = true;
            else if (isAlt) _altHeld = true;
            else if (_chordActive) _otherKeyDuringChord = true;

            // Win+Ctrl just became held
            if (!_chordActive && _winHeld && _ctrlHeld)
            {
                _chordActive = true;
                _otherKeyDuringChord = false;
                _screenshotFired = false;

                if (_altHeld)
                {
                    // All three down at once → screenshot, no voice
                    _screenshotFired = true;
                    try { ScreenshotTriggered?.Invoke(); } catch { }
                }
                else
                {
                    OnLowChordDown();
                }
            }
            // Alt arrived while Win+Ctrl already held → cancel voice, fire screenshot
            else if (_chordActive && !_screenshotFired && _winHeld && _ctrlHeld && _altHeld)
            {
                _screenshotFired = true;
                CancelVoiceLocked();
                try { ScreenshotTriggered?.Invoke(); } catch { }
            }
        }
        else if (isUp)
        {
            if (isWin) _winHeld = false;
            else if (isCtrl) _ctrlHeld = false;
            else if (isAlt) _altHeld = false;

            if (_chordActive && (!_winHeld || !_ctrlHeld))
            {
                _chordActive = false;
                bool clean = !_otherKeyDuringChord;
                _otherKeyDuringChord = false;
                bool wasScreenshot = _screenshotFired;
                _screenshotFired = false;

                if (!wasScreenshot)
                    OnLowChordUp(clean);
                // else: voice never started, nothing to release
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ===== state machine =====

    private void OnLowChordDown()
    {
        bool firePressed = false;
        bool fireReleased = false;
        bool releaseClean = true;

        lock (_smLock)
        {
            if (_mode == Mode.Toggle)
            {
                // Already toggled-on → this press is the "stop" tap.
                _mode = Mode.Idle;
                _recordingActive = false;
                fireReleased = true;
            }
            else if (_waitingForSecondTap)
            {
                // Second half of a quick double-tap → enter toggle mode.
                _waitingForSecondTap = false;
                CancelDoubleTapTimerLocked();
                _mode = Mode.Toggle;
                // Recording is already on (started by first press of the double-tap); keep it.
            }
            else
            {
                // Fresh press → start recording optimistically.
                _pressTimeUtc = DateTime.UtcNow;
                _recordingActive = true;
                firePressed = true;
            }
        }

        if (firePressed) try { ChordPressed?.Invoke(); } catch { }
        if (fireReleased) try { ChordReleased?.Invoke(releaseClean); } catch { }
    }

    private void OnLowChordUp(bool clean)
    {
        bool fireReleased = false;
        bool releaseClean = clean;

        lock (_smLock)
        {
            if (_mode == Mode.Toggle) return;       // releases are no-ops while toggled on
            if (!_recordingActive) return;          // we never started

            var heldMs = (DateTime.UtcNow - _pressTimeUtc).TotalMilliseconds;
            if (clean && heldMs < PttHoldMs)
            {
                // Possibly the first half of a double-tap. Wait briefly to see.
                _waitingForSecondTap = true;
                _firstTapClean = clean;
                CancelDoubleTapTimerLocked();
                _doubleTapTimer = new System.Threading.Timer(OnDoubleTapTimeout, null,
                    DoubleTapWindowMs, System.Threading.Timeout.Infinite);
            }
            else
            {
                _recordingActive = false;
                fireReleased = true;
            }
        }

        if (fireReleased) try { ChordReleased?.Invoke(releaseClean); } catch { }
    }

    private void OnDoubleTapTimeout(object? state)
    {
        bool fire = false;
        bool clean = true;

        lock (_smLock)
        {
            if (!_waitingForSecondTap) return;
            _waitingForSecondTap = false;
            CancelDoubleTapTimerLocked();
            _recordingActive = false;
            clean = _firstTapClean;
            fire = true;
        }

        if (fire) try { ChordReleased?.Invoke(clean); } catch { }
    }

    private void CancelDoubleTapTimerLocked()
    {
        _doubleTapTimer?.Dispose();
        _doubleTapTimer = null;
    }

    /// <summary>Aborts any voice recording (PTT or toggle) and tells the caller to discard.</summary>
    private void CancelVoiceLocked()
    {
        bool fireDirty = false;
        lock (_smLock)
        {
            if (_recordingActive || _mode == Mode.Toggle || _waitingForSecondTap)
            {
                fireDirty = _recordingActive || _mode == Mode.Toggle;
                _recordingActive = false;
                _mode = Mode.Idle;
                _waitingForSecondTap = false;
                CancelDoubleTapTimerLocked();
            }
        }
        if (fireDirty) try { ChordReleased?.Invoke(false); } catch { }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        lock (_smLock) CancelDoubleTapTimerLocked();
    }
}
