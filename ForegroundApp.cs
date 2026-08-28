using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GroqVoice;

/// <summary>
/// Works out what the keystrokes we are about to send will actually land in.
///
/// This matters because a remote-desktop client does not consume Ctrl+V itself —
/// it forwards the keystroke to the far machine, which then pastes from *its*
/// clipboard. Our text is on the local one. The clients all sync clipboards, but
/// asynchronously, and a Ctrl+V sent immediately after Clipboard.SetText wins the
/// race and pastes stale content or nothing.
///
/// Detection is necessarily a heuristic. Dedicated clients are recognised by
/// process name, which is solid. Chrome Remote Desktop runs inside chrome.exe and
/// can only be spotted by window title — which is localised, hence the
/// user-extendable marker list in config.
/// </summary>
public static class ForegroundApp
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>Clients that own their own window — process name is enough.</summary>
    private static readonly string[] RemoteProcesses =
    {
        "mstsc",          // Windows Remote Desktop
        "msrdc",          // Windows App / RD client
        "msrdcw",
        "anydesk",
        "teamviewer",
        "parsecd",
        "vncviewer",
        "tvnviewer",
        "winvnc",
        "remoting_desktop",
        "dwrcc",          // Dameware
        "rdcman",
    };

    /// <summary>
    /// Browser-hosted clients: title substrings, matched case-insensitively. Both
    /// Russian spellings are here because Chrome uses ё or е depending on build.
    /// </summary>
    private static readonly string[] TitleMarkers =
    {
        "chrome remote desktop",
        "удаленный рабочий стол chrome",
        "удалённый рабочий стол chrome",
        "escritorio remoto de chrome",
        "bureau à distance chrome",
    };

    private static readonly string[] BrowserProcesses = { "chrome", "msedge", "brave", "vivaldi", "opera" };

    public static string ProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    public static string WindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            var sb = new StringBuilder(512);
            int n = GetWindowTextW(hwnd, sb, sb.Capacity);
            return n > 0 ? sb.ToString() : "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// True if the focused window belongs to a remote-desktop session, i.e. our
    /// keystrokes will be forwarded to another machine rather than handled here.
    /// <paramref name="why"/> carries the matched signal for the log.
    /// </summary>
    public static bool IsRemoteSession(IEnumerable<string>? extraMarkers, out string why)
        => Classify(ProcessName(), WindowTitle(), extraMarkers, out why);

    /// <summary>Pure form of <see cref="IsRemoteSession"/>, so the rules can be tested.</summary>
    public static bool Classify(string proc, string title, IEnumerable<string>? extraMarkers, out string why)
    {
        why = "";
        if (string.IsNullOrEmpty(proc)) return false;

        foreach (var name in RemoteProcesses)
        {
            if (proc.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                why = $"process '{proc}'";
                return true;
            }
        }

        if (string.IsNullOrEmpty(title)) return false;

        bool isBrowser = BrowserProcesses.Any(b => proc.Equals(b, StringComparison.OrdinalIgnoreCase));
        var markers = extraMarkers is null ? TitleMarkers : TitleMarkers.Concat(extraMarkers);

        foreach (var m in markers)
        {
            if (string.IsNullOrWhiteSpace(m)) continue;
            if (title.Contains(m, StringComparison.OrdinalIgnoreCase))
            {
                // A user-supplied marker is trusted anywhere; the built-in ones only
                // count inside a browser, so an editor with "Chrome Remote Desktop"
                // in its title bar isn't mistaken for a session.
                bool builtin = TitleMarkers.Contains(m, StringComparer.OrdinalIgnoreCase);
                if (builtin && !isBrowser) continue;
                why = $"title of '{proc}' matches \"{m}\"";
                return true;
            }
        }

        return false;
    }

    public static string Describe()
    {
        var t = WindowTitle();
        return t.Length > 60 ? $"{ProcessName()} — {t[..60]}…" : $"{ProcessName()} — {t}";
    }
}
