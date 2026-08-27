using System.Text;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// A hotkey exactly as the user assigned it: an arbitrary set of keys held
/// together. No minimum, no maximum — one key is as valid as five. Stored in
/// config.json as plain text ("Win+Ctrl", "Ctrl+Shift+S", "F13") so it stays
/// hand-editable via "Open config (advanced)".
///
/// Left/right variants are folded together, so a combo assigned with the left
/// Ctrl also fires on the right one.
///
/// Note on Fn: laptop Fn keys are resolved by the keyboard's own controller and
/// never reach Windows as a scan code, so they cannot be captured. F13–F24,
/// which some keyboards emit for Fn combos, work fine.
/// </summary>
public sealed class HotkeyCombo : IEquatable<HotkeyCombo>
{
    // canonical modifier codes — left/right variants normalise onto these
    public const uint VkWin = 0x5B;      // Keys.LWin
    public const uint VkCtrl = 0x11;     // Keys.ControlKey
    public const uint VkAlt = 0x12;      // Keys.Menu
    public const uint VkShift = 0x10;    // Keys.ShiftKey

    public static readonly HotkeyCombo VoiceDefault = new(new[] { VkWin, VkCtrl });
    public static readonly HotkeyCombo ScreenshotDefault = new(new[] { VkWin, VkCtrl, VkAlt });
    public static readonly HotkeyCombo None = new(Array.Empty<uint>());

    /// <summary>Normalised, de-duplicated, in display order.</summary>
    public IReadOnlyList<uint> Keys { get; }

    public HotkeyCombo(IEnumerable<uint> keys)
    {
        var set = new List<uint>();
        foreach (var raw in keys)
        {
            var vk = Normalise(raw);
            if (vk == 0 || IsMouseOrInvalid(vk)) continue;
            if (!set.Contains(vk)) set.Add(vk);
        }
        set.Sort(CompareForDisplay);
        Keys = set;
    }

    public bool IsEmpty => Keys.Count == 0;

    /// <summary>
    /// Any non-empty set is accepted. A single ordinary key is a poor choice — it
    /// fires whenever that key is typed — but that is the user's call, not ours.
    /// </summary>
    public bool IsUsable => Keys.Count > 0;

    /// <summary>True if this combo would fire on ordinary typing.</summary>
    public bool IsRisky => Keys.Count == 1 && !IsModifierVk(Keys[0]) && !IsStandaloneKey(Keys[0]);

    public bool Contains(uint vk) => Keys.Contains(Normalise(vk));

    public static bool IsModifierVk(uint vk) =>
        vk is VkWin or VkCtrl or VkAlt or VkShift;

    /// <summary>Mouse buttons never belong in a keyboard hotkey; 0x00/0x07/0xFF are not real keys.</summary>
    public static bool IsMouseOrInvalid(uint vk) =>
        vk is 0x00                      // none
           or 0x01 or 0x02 or 0x04      // left / right / middle mouse
           or 0x05 or 0x06              // mouse X1 / X2
           or 0x07 or 0xFF;             // undefined / VK_NONE (injected by some KVMs and RDP)

    /// <summary>Folds LCtrl/RCtrl → Ctrl, LWin/RWin → Win, and so on.</summary>
    public static uint Normalise(uint vk) => vk switch
    {
        0x5B or 0x5C => VkWin,
        0xA2 or 0xA3 or 0x11 => VkCtrl,
        0xA4 or 0xA5 or 0x12 => VkAlt,
        0xA0 or 0xA1 or 0x10 => VkShift,
        _ => vk,
    };

    // modifiers first and in a stable order, then everything else by code
    private static int CompareForDisplay(uint a, uint b)
    {
        int ra = DisplayRank(a), rb = DisplayRank(b);
        return ra != rb ? ra.CompareTo(rb) : a.CompareTo(b);
    }

    private static int DisplayRank(uint vk) => vk switch
    {
        VkWin => 0, VkCtrl => 1, VkAlt => 2, VkShift => 3, _ => 4,
    };

    private static bool IsStandaloneKey(uint vk) =>
        vk is >= (uint)System.Windows.Forms.Keys.F13 and <= (uint)System.Windows.Forms.Keys.F24
           or (uint)System.Windows.Forms.Keys.Pause
           or (uint)System.Windows.Forms.Keys.Scroll;

    public override string ToString()
    {
        if (IsEmpty) return "(none)";
        var sb = new StringBuilder();
        foreach (var vk in Keys)
        {
            if (sb.Length > 0) sb.Append('+');
            sb.Append(KeyName(vk));
        }
        return sb.ToString();
    }

    private static string KeyName(uint vk)
    {
        switch (vk)
        {
            case VkWin: return "Win";
            case VkCtrl: return "Ctrl";
            case VkAlt: return "Alt";
            case VkShift: return "Shift";
        }

        var n = ((Keys)vk).ToString();
        // D0..D9 are the number-row digits; show them as plain digits.
        if (n.Length == 2 && n[0] == 'D' && char.IsDigit(n[1])) return n[1].ToString();
        if (n.StartsWith("Oem", StringComparison.Ordinal) && n.Length > 3) return n[3..];
        // an unnamed code comes back as the bare number — show it as hex so it round-trips
        if (uint.TryParse(n, out _)) return "0x" + vk.ToString("X2");
        return n;
    }

    public static HotkeyCombo Parse(string? text, HotkeyCombo fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        var keys = new List<uint>();
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;

            switch (t.ToLowerInvariant())
            {
                case "win" or "windows" or "meta" or "super": keys.Add(VkWin); continue;
                case "ctrl" or "control": keys.Add(VkCtrl); continue;
                case "alt" or "menu": keys.Add(VkAlt); continue;
                case "shift": keys.Add(VkShift); continue;
            }

            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(t[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex))
            {
                keys.Add(hex);
                continue;
            }

            if (t.Length == 1 && char.IsDigit(t[0])) t = "D" + t;
            if (Enum.TryParse<Keys>(t, ignoreCase: true, out var parsed) && parsed != System.Windows.Forms.Keys.None)
            {
                keys.Add((uint)parsed);
                continue;
            }

            Log.Info($"hotkey parse: unrecognised token \"{raw}\" in \"{text}\" — ignored");
        }

        var combo = new HotkeyCombo(keys);
        if (!combo.IsUsable)
        {
            Log.Info($"hotkey parse: \"{text}\" yielded no usable keys — falling back to {fallback}");
            return fallback;
        }
        return combo;
    }

    public bool Equals(HotkeyCombo? other)
    {
        if (other is null || other.Keys.Count != Keys.Count) return false;
        for (int i = 0; i < Keys.Count; i++)
            if (Keys[i] != other.Keys[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as HotkeyCombo);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var k in Keys) h.Add(k);
        return h.ToHashCode();
    }

    public static bool operator ==(HotkeyCombo? a, HotkeyCombo? b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(HotkeyCombo? a, HotkeyCombo? b) => !(a == b);
}
