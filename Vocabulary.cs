namespace GroqVoice;

/// <summary>
/// Loads %APPDATA%\GroqVoice\vocabulary.txt and turns it into a Whisper "prompt"
/// string that biases the STT toward task-specific terms.
///
/// File format: one word or short phrase per line. Lines starting with '#' are comments.
/// Whisper accepts up to ~224 tokens; we keep the prompt under ~700 chars for safety.
/// Re-read on every call only if the file's mtime changed — cheap and hot-reloads
/// without needing to restart the app.
/// </summary>
public static class Vocabulary
{
    public static string Path => System.IO.Path.Combine(Config.Dir, "vocabulary.txt");
    private const int MaxPromptChars = 700;

    private static readonly object _gate = new();
    private static DateTime _cachedMtime = DateTime.MinValue;
    private static string _cachedPrompt = "";
    private static int _cachedCount = 0;

    public static void EnsureFileExists()
    {
        if (File.Exists(Path)) return;
        Directory.CreateDirectory(Config.Dir);
        File.WriteAllText(Path,
            "# GroqVoice vocabulary — biases Whisper toward task-specific words.\n" +
            "# One word or short phrase per line. Capitalisation matters.\n" +
            "# Lines starting with '#' are ignored. Hot-reloads — no restart needed.\n" +
            "#\n" +
            "# Examples:\n" +
            "# Resonance\n" +
            "# WhiteBIT\n" +
            "# FlashBot\n" +
            "# OAuth\n" +
            "# gRPC\n" +
            "# Postgres\n");
    }

    /// <summary>Returns the prompt string and the number of vocabulary entries used.</summary>
    public static (string prompt, int count) LoadPrompt()
    {
        lock (_gate)
        {
            try
            {
                EnsureFileExists();
                var mtime = File.GetLastWriteTimeUtc(Path);
                if (mtime == _cachedMtime) return (_cachedPrompt, _cachedCount);

                var lines = File.ReadAllLines(Path);
                var terms = new List<string>(lines.Length);
                foreach (var raw in lines)
                {
                    var s = raw.Trim();
                    if (s.Length == 0 || s[0] == '#') continue;
                    terms.Add(s);
                }

                // Comma-separated terms: Whisper picks up vocabulary best when entries are
                // listed naturally rather than as a sentence.
                string prompt;
                if (terms.Count == 0)
                {
                    prompt = "";
                }
                else
                {
                    var joined = string.Join(", ", terms);
                    if (joined.Length > MaxPromptChars)
                    {
                        // truncate at last comma boundary that fits, so we don't cut a word in half
                        int cut = joined.LastIndexOf(", ", MaxPromptChars, StringComparison.Ordinal);
                        prompt = cut > 0 ? joined[..cut] : joined[..MaxPromptChars];
                    }
                    else prompt = joined;
                }

                _cachedMtime = mtime;
                _cachedPrompt = prompt;
                _cachedCount = terms.Count;
                return (prompt, terms.Count);
            }
            catch (Exception ex)
            {
                Log.Warn($"vocabulary load failed: {ex.Message}");
                return ("", 0);
            }
        }
    }
}
