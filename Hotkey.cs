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

    /// <summary>User-assigned chord for dictation. Modifier-only combos keep the PTT / double-tap semantics.</summary>
    public HotkeyCombo VoiceCombo { get; set; } = HotkeyCombo.VoiceDefault;
    /// <summary>User-assigned chord for the snip. Takes precedence over <see cref="VoiceCombo"/>.</summary>
    public HotkeyCombo ScreenshotCombo { get; set; } = HotkeyCombo.ScreenshotDefault;

    public event Action? ChordPressed;
    /// <summary>clean=false means a third key was pressed during the chord.</summary>
    public event Action<bool>? ChordReleased;
    /// <summary>Fires once when the screenshot combo becomes held. Cancels any in-progress voice recording.</summary>
    public event Action? ScreenshotTriggered;

    /// <summary>
    /// While set, no action fires and every key is swallowed (except Esc, so the
    /// capture dialog stays cancellable); the keys being pressed are reported via
    /// <see cref="CaptureUpdated"/> instead.
    /// </summary>
    public bool CaptureMode
    {
        get => _captureMode;
        set
        {
            _captureMode = value;
            if (!value)
            {
                // Leaving the dialog: treat everything as released. The keys used to
                // assign the combo may still be down, and the combo itself may have
                // just changed — neither should fire an action on the way out.
                _voiceHeldPrev = false;
                _shotHeldPrev = false;
                _otherKeyDuringChord = false;
                _awaitAllReleased = true;
            }
        }
    }
    private bool _captureMode;
    /// <summary>Reports the combo currently being pressed while <see cref="CaptureMode"/> is on.</summary>
    public event Action<HotkeyCombo>? CaptureUpdated;

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
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_RETURN = 0x0D;

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
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>True if either of the two virtual keys is physically down right now.</summary>
    private static bool PhysDown(int vkA, int vkB) =>
        ((GetAsyncKeyState(vkA) | GetAsyncKeyState(vkB)) & 0x8000) != 0;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    // chord-detection (low-level)
    private bool _winHeld;
    private bool _ctrlHeld;
    private bool _altHeld;
    private bool _shiftHeld;
    private bool _voiceHeldPrev;     // edge detection for the dictation combo
    private bool _shotHeldPrev;      // edge detection for the screenshot combo
    private bool _otherKeyDuringChord;
    private bool _awaitAllReleased;  // after a snip: stay disarmed until every key is physically up

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
        bool isShift = vk == VK_LSHIFT || vk == VK_RSHIFT;
        bool isModifier = isWin || isCtrl || isAlt || isShift;

        // Re-sync the held-flags from the real keyboard on every event. Windows
        // silently skips a low-level hook that overruns LowLevelHooksTimeout, so
        // individual key-ups do go missing — most easily right after a snip, while
        // the full-virtual-screen grab occupies this same thread. Accumulated flags
        // would then stay stuck "held" and a later lone modifier press would
        // re-satisfy the chord and fire a second screenshot. Physical state wins;
        // the in-flight event is layered on top because the hook runs before the
        // system updates the async key state for this key.
        _winHeld = PhysDown(VK_LWIN, VK_RWIN);
        _ctrlHeld = PhysDown(VK_LCONTROL, VK_RCONTROL);
        _altHeld = PhysDown(VK_LMENU, VK_RMENU);
        _shiftHeld = PhysDown(VK_LSHIFT, VK_RSHIFT);
        if (isDown || isUp)
        {
            if (isWin) _winHeld = isDown;
            else if (isCtrl) _ctrlHeld = isDown;
            else if (isAlt) _altHeld = isDown;
            else if (isShift) _shiftHeld = isDown;
        }

        // ---- assignment dialog: report, swallow, do nothing else --------------
        if (CaptureMode)
        {
            // Esc and a bare Enter still travel, so Cancel and OK stay reachable from
            // the keyboard. With a modifier held Enter is fair game as part of a combo.
            bool bareEnter = vk == VK_RETURN && !_winHeld && !_ctrlHeld && !_altHeld && !_shiftHeld;
            if (vk == VK_ESCAPE || bareEnter) return CallNextHookEx(_hookId, nCode, wParam, lParam);
            if (isDown)
            {
                var seen = new HotkeyCombo(_winHeld, _ctrlHeld, _altHeld, _shiftHeld,
                                           isModifier ? 0u : vk);
                try { CaptureUpdated?.Invoke(seen); } catch { }
            }
            return (IntPtr)1;   // never let the combo reach the app underneath
        }

        var voice = VoiceCombo;
        var shot = ScreenshotCombo;

        // After a screenshot every key counts as released, and both chords stay
        // disarmed until the user has genuinely let go of everything.
        if (_awaitAllReleased)
        {
            if (_winHeld || _ctrlHeld || _altHeld || _shiftHeld
                || KeyHeld(voice.Key, vk, isDown, isUp) || KeyHeld(shot.Key, vk, isDown, isUp))
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            _awaitAllReleased = false;
        }

        // ---- screenshot wins over dictation -----------------------------------
        bool shotHeld = ComboHeld(shot, vk, isDown, isUp);
        if (shotHeld && !_shotHeldPrev)
        {
            _shotHeldPrev = true;
            // Adding Alt to a held Win+Ctrl lands here: abandon the half-spoken
            // phrase (dirty release = caller discards) and commit to photo mode.
            CancelVoiceLocked();
            _voiceHeldPrev = false;
            _otherKeyDuringChord = false;
            FireScreenshot();
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        _shotHeldPrev = shotHeld;

        // ---- dictation --------------------------------------------------------
        // Driven by re-synced state rather than by whichever key the event carried,
        // so a dropped key-up cannot strand the chord open.
        bool voiceHeld = ComboHeld(voice, vk, isDown, isUp);
        if (voiceHeld && !_voiceHeldPrev)
        {
            _voiceHeldPrev = true;
            _otherKeyDuringChord = false;
            OnLowChordDown();
        }
        else if (!voiceHeld && _voiceHeldPrev)
        {
            _voiceHeldPrev = false;
            bool clean = !_otherKeyDuringChord;
            _otherKeyDuringChord = false;
            OnLowChordUp(clean);
        }
        else if (voiceHeld && isDown && !isModifier && vk != voice.Key)
        {
            // A third key rode along — the caller discards the audio.
            _otherKeyDuringChord = true;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Hands the snip off to the caller and immediately winds the chord down as if
    /// every modifier had been released, so the keys the user is still physically
    /// holding cannot trigger a second screenshot.
    /// </summary>
    private void FireScreenshot()
    {
        _voiceHeldPrev = false;
        _shotHeldPrev = false;
        _otherKeyDuringChord = false;
        _winHeld = _ctrlHeld = _altHeld = _shiftHeld = false;
        _awaitAllReleased = true;

        try { ScreenshotTriggered?.Invoke(); } catch { }
    }

    /// <summary>
    /// Modifiers must match exactly — that is what keeps Win+Ctrl and Win+Ctrl+Alt
    /// apart, so adding Alt ends the dictation match and begins the screenshot one.
    /// </summary>
    private bool ComboHeld(HotkeyCombo c, uint eventVk, bool isDown, bool isUp)
    {
        if (c.IsEmpty) return false;
        return c.Win == _winHeld && c.Ctrl == _ctrlHeld
            && c.Alt == _altHeld && c.Shift == _shiftHeld
            && (!c.HasKey || KeyHeld(c.Key, eventVk, isDown, isUp));
    }

    /// <summary>Physical state of one key, with the in-flight event layered on top.</summary>
    private static bool KeyHeld(uint vk, uint eventVk, bool isDown, bool isUp)
    {
        if (vk == 0) return false;
        if (vk == eventVk && (isDown || isUp)) return isDown;
        return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
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
