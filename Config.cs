using System.Text.Json;
using System.Text.Json.Serialization;

namespace GroqVoice;

public sealed class Config
{
    [JsonPropertyName("groqApiKey")] public string GroqApiKey { get; set; } = "";
    [JsonPropertyName("transcriptionModel")] public string TranscriptionModel { get; set; } = "whisper-large-v3";
    [JsonPropertyName("chatModel")] public string ChatModel { get; set; } = "llama-3.3-70b-versatile";

    // empty = auto-detect; whisper handles ru/en code-switching well in auto mode
    [JsonPropertyName("language")] public string Language { get; set; } = "";

    [JsonPropertyName("taskKeywords")] public string[] TaskKeywords { get; set; } =
        new[] { "task", "задача", "задание" };

    // a task keyword counts only if it appears within the first N words of the transcript;
    // otherwise the utterance is treated as plain dictation. Default 4.
    [JsonPropertyName("taskKeywordMaxWordPosition")] public int TaskKeywordMaxWordPosition { get; set; } = 4;

    [JsonPropertyName("autostart")] public bool Autostart { get; set; } = true;
    [JsonPropertyName("playFeedbackSounds")] public bool PlayFeedbackSounds { get; set; } = true;

    // optional system prompt override; if blank, default in code is used
    [JsonPropertyName("taskSystemPrompt")] public string TaskSystemPrompt { get; set; } = "";

    // empty = system default mic; otherwise case-insensitive substring of the device's product name
    [JsonPropertyName("inputDeviceContains")] public string InputDeviceContains { get; set; } = "";

    // when true, every recording is dumped to %APPDATA%\GroqVoice\last.wav for debugging
    [JsonPropertyName("saveLastWav")] public bool SaveLastWav { get; set; } = true;

    // recordings shorter than this are dropped without contacting Groq
    [JsonPropertyName("minRecordingSeconds")] public double MinRecordingSeconds { get; set; } = 1.0;

    // peak amplitude (% of full scale) below which a recording is treated as silent and dropped
    [JsonPropertyName("silencePeakPercent")] public double SilencePeakPercent { get; set; } = 1.0;

    // user-assignable hotkeys, as text ("Win+Ctrl", "Ctrl+Shift+S", "F13").
    // Modifier-only combos need two modifiers; anything unparsable falls back to
    // the default and is noted in the log. Laptop Fn keys cannot be bound —
    // they are resolved in keyboard firmware and never reach Windows.
    [JsonPropertyName("voiceHotkey")] public string VoiceHotkey { get; set; } = "Win+Ctrl";
    [JsonPropertyName("screenshotHotkey")] public string ScreenshotHotkey { get; set; } = "Win+Ctrl+Alt";

    // chord held longer than this is treated as push-to-talk; shorter is a "tap"
    [JsonPropertyName("pttHoldMs")] public int PttHoldMs { get; set; } = 250;

    // a second tap arriving within this window after a quick first tap = double-tap toggle
    [JsonPropertyName("doubleTapWindowMs")] public int DoubleTapWindowMs { get; set; } = 400;

    // after Win+Ctrl+Alt snip, open the annotator window immediately
    // (bypasses the easily-missed Windows notification balloon)
    [JsonPropertyName("autoOpenAnnotator")] public bool AutoOpenAnnotator { get; set; } = true;

    // when true (legacy), restore the previous clipboard ~250 ms after Ctrl+V paste.
    // when false (default), the dictated text stays on the clipboard so it can be
    // re-pasted manually — essential for Chrome Remote Desktop / RDP / sluggish apps
    // where the initial Ctrl+V may not reach the remote side in time.
    [JsonPropertyName("restoreClipboardAfterPaste")] public bool RestoreClipboardAfterPaste { get; set; } = false;

    // "auto"  — paste normally, but wait for clipboard sync when the focused window
    //           looks like a remote-desktop session (default)
    // "paste" — always Ctrl+V immediately, no detection
    // "type"  — send the text as literal keystrokes instead of pasting
    [JsonPropertyName("pasteMode")] public string PasteMode { get; set; } = "auto";

    // head start given to a remote client's clipboard sync before Ctrl+V is sent
    [JsonPropertyName("remotePasteDelayMs")] public int RemotePasteDelayMs { get; set; } = 800;

    // Remote clients read the local clipboard when their window gains focus, not when
    // the clipboard changes — so dictating without ever leaving the session pastes
    // whatever was there when the window was entered. When true, the window's focus is
    // cycled before Ctrl+V so the client re-reads. Set false if it misbehaves.
    [JsonPropertyName("remoteClipboardFocusNudge")] public bool RemoteClipboardFocusNudge { get; set; } = true;

    // extra window-title substrings that mark a remote session. The built-in list
    // covers Chrome Remote Desktop in several languages; add your own if the title
    // differs — matching is case-insensitive.
    [JsonPropertyName("remoteWindowMarkers")] public string[] RemoteWindowMarkers { get; set; } =
        Array.Empty<string>();

    public static string Dir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GroqVoice");

    public static string Path => System.IO.Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        Directory.CreateDirectory(Dir);

        // Migrate legacy env-var key if present and config missing
        if (!File.Exists(Path))
        {
            var c = new Config
            {
                GroqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? ""
            };
            c.Save();
            return c;
        }

        try
        {
            var json = File.ReadAllText(Path);
            var cfg = JsonSerializer.Deserialize<Config>(json) ?? new Config();
            if (string.IsNullOrWhiteSpace(cfg.GroqApiKey))
                cfg.GroqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";
            return cfg;
        }
        catch (Exception ex)
        {
            Log.Error($"config parse failed at {Path} — using defaults", ex);
            return new Config();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path, json);
    }
}
