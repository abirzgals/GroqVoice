using System.Text;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// A hotkey exactly as the user assigned it: a set of modifiers plus an optional
/// main key. Stored in config.json as plain text ("Win+Ctrl", "Ctrl+Shift+S",
/// "F13") so it stays hand-editable via "Open config (advanced)".
///
/// Note on Fn: laptop Fn keys are resolved by the keyboard's own controller and
/// never reach Windows as a scan code, so they cannot be captured here. F13–F24,
/// which some keyboards emit for Fn combos, work fine.
/// </summary>
public sealed record HotkeyCombo(bool Win, bool Ctrl, bool Alt, bool Shift, uint Key)
{
    public static readonly HotkeyCombo VoiceDefault = new(true, true, false, false, 0);
    public static readonly HotkeyCombo ScreenshotDefault = new(true, true, true, false, 0);
    public static readonly HotkeyCombo None = new(false, false, false, false, 0);

    public bool HasKey => Key != 0;
    public bool IsEmpty => !Win && !Ctrl && !Alt && !Shift && !HasKey;
    public int ModifierCount => (Win ? 1 : 0) + (Ctrl ? 1 : 0) + (Alt ? 1 : 0) + (Shift ? 1 : 0);

    /// <summary>
    /// A modifier-only hotkey needs at least two modifiers — a lone Ctrl would fire
    /// on every ordinary keystroke. With a main key a single modifier is fine, and
    /// a bare function key (F13…) is fine on its own.
    /// </summary>
    public bool IsUsable => HasKey ? (ModifierCount >= 1 || IsStandaloneKey) : ModifierCount >= 2;

    /// <summary>Keys safe to bind with no modifier at all — they type nothing.</summary>
    private bool IsStandaloneKey =>
        Key is >= (uint)Keys.F13 and <= (uint)Keys.F24 or (uint)Keys.Pause or (uint)Keys.Scroll;

    public override string ToString()
    {
        if (IsEmpty) return "(none)";
        var sb = new StringBuilder();
        if (Win) sb.Append("Win+");
        if (Ctrl) sb.Append("Ctrl+");
        if (Alt) sb.Append("Alt+");
        if (Shift) sb.Append("Shift+");
        if (HasKey) sb.Append(KeyName(Key));
        else sb.Length--;   // drop the trailing '+'
        return sb.ToString();
    }

    private static string KeyName(uint vk)
    {
        var k = (Keys)vk;
        var n = k.ToString();
        // D0..D9 are the number-row digits; show them as plain digits.
        if (n.Length == 2 && n[0] == 'D' && char.IsDigit(n[1])) return n[1].ToString();
        if (n.StartsWith("Oem", StringComparison.Ordinal)) return n[3..];
        return n;
    }

    public static HotkeyCombo Parse(string? text, HotkeyCombo fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        bool win = false, ctrl = false, alt = false, shift = false;
        uint key = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;

            switch (t.ToLowerInvariant())
            {
                case "win" or "windows" or "meta" or "super": win = true; continue;
                case "ctrl" or "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
            }

            if (t.Length == 1 && char.IsDigit(t[0])) t = "D" + t;
            if (Enum.TryParse<Keys>(t, ignoreCase: true, out var parsed) && parsed != Keys.None)
            {
                key = (uint)parsed;
                continue;
            }

            Log.Info($"hotkey parse: unrecognised token \"{raw}\" in \"{text}\" — ignored");
        }

        var combo = new HotkeyCombo(win, ctrl, alt, shift, key);
        if (!combo.IsUsable)
        {
            Log.Info($"hotkey parse: \"{text}\" is not a usable combo — falling back to {fallback}");
            return fallback;
        }
        return combo;
    }
}
