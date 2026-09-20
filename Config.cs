using System.Text.Json;
using System.Text.Json.Serialization;

namespace GroqVoice;

public sealed class Config
{
    [JsonPropertyName("groqApiKey")] public string GroqApiKey { get; set; } = "";
    [JsonPropertyName("transcriptionModel")] public string TranscriptionModel { get; set; } = "whisper-large-v3";
    // Checked against the live Groq API on 2026-09-20: the Llama 3.x models this
    // app shipped with have been retired. Qwen goes first — on a one-sentence
    // rewrite it answers in a fraction of the tokens the gpt-oss models spend on
    // hidden reasoning, and the free tier meters tokens per minute.
    [JsonPropertyName("chatModel")] public string ChatModel { get; set; } = "qwen/qwen3.8-27b";

    // Which engine transcribes: "groq" (Whisper in the cloud, the default this
    // app shipped with) or "parakeet" (NVIDIA Parakeet TDT v3 on this machine —
    // offline, no API key, ~0.3 s per phrase, one-time 460 MB model download).
    // Same names as the macOS build, so a config is readable on both.
    [JsonPropertyName("sttEngine")] public string SttEngine { get; set; } = "groq";

    // If the chosen engine can't serve a take (model missing, no network, API
    // error), try the other one instead of failing the dictation.
    [JsonPropertyName("sttFallback")] public bool SttFallback { get; set; } = true;

    // Minutes of idle before the local model is dropped from RAM (~1.4 GB);
    // 0 = keep it warm, which is what makes the next dictation instant.
    [JsonPropertyName("localUnloadAfterMinutes")] public double LocalUnloadAfterMinutes { get; set; } = 0;

    // empty = auto-detect; whisper handles ru/en code-switching well in auto mode
    [JsonPropertyName("language")] public string Language { get; set; } = "";

    [JsonPropertyName("taskKeywords")] public string[] TaskKeywords { get; set; } =
        new[] { "task", "задача", "задание" };

    // a task keyword counts only if it appears within the first N words of the transcript;
    // otherwise the utterance is treated as plain dictation. Default 4.
    [JsonPropertyName("taskKeywordMaxWordPosition")] public int TaskKeywordMaxWordPosition { get; set; } = 4;

    // How many past results are kept in history.jsonl, so a paste that landed in
    // the wrong window can be recovered from the tray. 0 disables history.
    [JsonPropertyName("historySize")] public int HistorySize { get; set; } = 50;

    // When text is selected in the focused app, treat the utterance as an
    // instruction about it ("сделай короче", "переведи на английский") and replace
    // the selection with the result. Costs one Ctrl+C probe per dictation and
    // needs an LLM, so it is skipped entirely without a Groq key.
    [JsonPropertyName("editSelection")] public bool EditSelection { get; set; } = true;

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

    // Remote clients read the local clipboard when their window is ACTIVATED, not when
    // the clipboard changes — so dictating without ever leaving the session pastes
    // whatever was there when the window was entered.
    //
    // Off by default. Forcing the activation means taking the foreground away and
    // giving it back, and Windows' foreground lock makes that unreliable from a
    // background process: measured here, one run moved the foreground and could not
    // restore it (focus stranded on the taskbar), the next could not move it at all.
    // Enable only if it proves to behave on your machine.
    [JsonPropertyName("remoteClipboardFocusNudge")] public bool RemoteClipboardFocusNudge { get; set; } = false;

    // extra window-title substrings that mark a remote session. The built-in list
    // covers Chrome Remote Desktop in several languages; add your own if the title
    // differs — matching is case-insensitive.
    [JsonPropertyName("remoteWindowMarkers")] public string[] RemoteWindowMarkers { get; set; } =
        Array.Empty<string>();

    [JsonIgnore]
    public bool UsesLocalEngine =>
        string.Equals(SttEngine, "parakeet", StringComparison.OrdinalIgnoreCase);

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
            var json = ReadWithRetry(Path);
            var cfg = JsonSerializer.Deserialize<Config>(json) ?? new Config();
            if (string.IsNullOrWhiteSpace(cfg.GroqApiKey))
                cfg.GroqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";
            cfg.MigrateRetiredModels();
            return cfg;
        }
        catch (Exception ex)
        {
            Log.Error($"config parse failed at {Path} — using defaults", ex);
            return new Config();
        }
    }

    /// <summary>
    /// Points a config at a model that still exists. Groq retires models, and a
    /// config written a few months ago silently fails every task and every edit —
    /// the request is simply refused. Only ids known to be gone are touched.
    /// </summary>
    private void MigrateRetiredModels()
    {
        if (ChatModel.StartsWith("llama-3.", StringComparison.OrdinalIgnoreCase) ||
            ChatModel.StartsWith("llama3", StringComparison.OrdinalIgnoreCase) ||
            ChatModel.StartsWith("mixtral", StringComparison.OrdinalIgnoreCase))
        {
            var old = ChatModel;
            ChatModel = new Config().ChatModel;
            Log.Warn($"chatModel \"{old}\" is retired on Groq — using \"{ChatModel}\". " +
                     "Change it in Settings if you prefer another.");
            Save();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        WriteWithRetry(Path, json);
    }

    // Another instance (a --transcribe run, a second startup) can hold the file for
    // the microseconds it takes to read or write it. Retrying costs nothing and keeps
    // a collision from being read as "the config is broken" — which would hand the
    // app an empty API key and then save the defaults over a good file.
    private const int FileAttempts = 5;
    private const int FileRetryMs = 60;

    private static string ReadWithRetry(string path)
    {
        for (int i = 1; ; i++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (i < FileAttempts) { Thread.Sleep(FileRetryMs); }
        }
    }

    private static void WriteWithRetry(string path, string text)
    {
        for (int i = 1; ; i++)
        {
            try { File.WriteAllText(path, text); return; }
            catch (IOException) when (i < FileAttempts) { Thread.Sleep(FileRetryMs); }
            catch (IOException ex) { Log.Warn($"could not save config: {ex.Message}"); return; }
        }
    }
}
