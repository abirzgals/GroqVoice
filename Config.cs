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

    // chord held longer than this is treated as push-to-talk; shorter is a "tap"
    [JsonPropertyName("pttHoldMs")] public int PttHoldMs { get; set; } = 250;

    // a second tap arriving within this window after a quick first tap = double-tap toggle
    [JsonPropertyName("doubleTapWindowMs")] public int DoubleTapWindowMs { get; set; } = 400;

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
