using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GroqVoice;

public sealed class Groq
{
    // single shared HttpClient, long-lived
    private static readonly HttpClient _http = new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    {
        BaseAddress = new Uri("https://api.groq.com/"),
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly Config _cfg;
    public Groq(Config cfg) { _cfg = cfg; }

    public async Task<string> TranscribeAsync(byte[] wav, string? vocabularyPrompt = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.GroqApiKey))
            throw new InvalidOperationException("Groq API key not configured.");

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "audio.wav");
        content.Add(new StringContent(_cfg.TranscriptionModel), "model");
        content.Add(new StringContent("text"), "response_format");
        if (!string.IsNullOrWhiteSpace(_cfg.Language))
            content.Add(new StringContent(_cfg.Language), "language");
        if (!string.IsNullOrWhiteSpace(vocabularyPrompt))
            content.Add(new StringContent(vocabularyPrompt), "prompt");

        using var req = new HttpRequestMessage(HttpMethod.Post, "openai/v1/audio/transcriptions") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.GroqApiKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Groq STT {(int)resp.StatusCode}: {Truncate(body, 400)}");
        return body.Trim();
    }

    private const string DefaultSystemPrompt =
        "You are a coding-focused assistant invoked from a developer's voice command. " +
        "The user is a software developer working on Windows. " +
        "The user's message is a transcribed voice command in Russian and/or English. " +
        "Reply in the language the user used. Be terse. " +
        "If asked for code, output only code with no fences or commentary unless explicitly requested. " +
        "If asked for translation, output only the translation. " +
        "If asked for ASCII art, output only the art. " +
        "Never preface with 'Sure' / 'Конечно' / restate the request.";

    /// <summary>
    /// System prompt for "text was selected when the key went down": the utterance
    /// is either an instruction about that text or a replacement for it, and the
    /// model decides which. Shared wording with the macOS build.
    /// </summary>
    public const string EditSelectionSystemPrompt =
        "You are the editing stage of a voice-dictation tool. The user selected some text in an " +
        "application, held the dictation key and spoke. You receive the selected text and the " +
        "transcript of what they said. Decide which of two things happened:\n" +
        "(A) They gave an instruction about the selected text — rewrite, shorten, expand, translate, " +
        "fix grammar or typos, change tone, reformat, summarize, continue it, answer a question it " +
        "contains, and so on. Then apply the instruction to the selected text and output ONLY the " +
        "resulting text that should replace the selection.\n" +
        "(B) They simply dictated new content to put in place of the selection (it reads as content, " +
        "not as a command about the text). Then output the spoken text exactly as transcribed.\n" +
        "Keep the selected text's language unless asked to translate; preserve its line breaks and " +
        "formatting when editing. Never explain your choice, never add quotes, notes or alternatives.";

    public static string EditSelectionUserMessage(string selection, string spoken) =>
        $"SELECTED TEXT:\n<<<\n{selection}\n>>>\n\nSPOKEN:\n{spoken}";

    public async Task<string> ChatAsync(string userMsg, CancellationToken ct = default) =>
        await ChatAsync(userMsg, systemPrompt: null, ct).ConfigureAwait(false);

    public async Task<string> ChatAsync(string userMsg, string? systemPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.GroqApiKey))
            throw new InvalidOperationException("Groq API key not configured.");

        var sys = systemPrompt
            ?? (string.IsNullOrWhiteSpace(_cfg.TaskSystemPrompt) ? DefaultSystemPrompt : _cfg.TaskSystemPrompt);

        var payload = new Dictionary<string, object>
        {
            ["model"] = _cfg.ChatModel,
            ["temperature"] = 0.2,
            // Groq rejects a request up front when the model's *default* output
            // budget exceeds the tier's tokens-per-minute limit — a one-word
            // translation was refused as "1106 expected output tokens" against a
            // 1000 limit. Asking for what the job actually needs both fixes that
            // and stops a runaway answer.
            ["max_tokens"] = OutputBudget(userMsg),
            ["messages"] = new object[]
            {
                new { role = "system", content = sys },
                new { role = "user",   content = userMsg },
            },
        };
        // gpt-oss "thinks" before answering and bills those tokens; rewriting a
        // sentence does not need deep reasoning.
        if (_cfg.ChatModel.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase))
            payload["reasoning_effort"] = "low";

        var json = JsonSerializer.Serialize(payload);

        using var req = new HttpRequestMessage(HttpMethod.Post, "openai/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.GroqApiKey);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Groq chat {(int)resp.StatusCode}: {Truncate(body, 400)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    /// <summary>
    /// Room for the answer, in tokens. These jobs rewrite or translate their input,
    /// so the answer is about the size of what went in; Cyrillic runs about two
    /// characters per token, which makes chars/2 a safe over-estimate. The floor
    /// covers one-word commands, the ceiling keeps a confused model from billing
    /// for pages.
    /// </summary>
    private static int OutputBudget(string input) => Math.Clamp(input.Length / 2 + 300, 300, 4096);

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
