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

    public async Task<string> ChatAsync(string userMsg, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.GroqApiKey))
            throw new InvalidOperationException("Groq API key not configured.");

        var sys = string.IsNullOrWhiteSpace(_cfg.TaskSystemPrompt) ? DefaultSystemPrompt : _cfg.TaskSystemPrompt;
        var payload = new
        {
            model = _cfg.ChatModel,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = sys },
                new { role = "user",   content = userMsg },
            }
        };
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

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
