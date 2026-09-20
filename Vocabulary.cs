using System.Text.RegularExpressions;

namespace GroqVoice;

/// <summary>
/// Loads %APPDATA%\GroqVoice\vocabulary.txt and serves it two ways:
///
/// * <see cref="LoadPrompt"/> — a Whisper "prompt" string that biases the Groq
///   engine toward task-specific terms.
/// * <see cref="ApplyAliases"/> — deterministic alias → term replacement for the
///   on-device engine, which accepts no prompt at all. Same idea as the macOS
///   build, and the same file format, so one vocabulary.txt serves both.
///
/// File format: one word or short phrase per line, optionally with the spellings
/// the recognizer tends to produce — "Coolify: кулифай, кулифи". Lines starting
/// with '#' are comments. Whisper accepts up to ~224 tokens; we keep the prompt
/// under ~700 chars for safety. Re-read on every call only if the file's mtime
/// changed — cheap and hot-reloads without needing to restart the app.
/// </summary>
public static class Vocabulary
{
    public static string Path => System.IO.Path.Combine(Config.Dir, "vocabulary.txt");
    private const int MaxPromptChars = 700;

    private static readonly object _gate = new();
    private static DateTime _cachedMtime = DateTime.MinValue;
    private static string _cachedPrompt = "";
    private static int _cachedCount = 0;
    private static List<(Regex rx, string term)> _cachedMatchers = new();

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
            "# OAuth\n" +
            "# gRPC\n" +
            "# Postgres\n" +
            "# Kubernetes\n" +
            "# WebSocket\n");
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
                var matchers = new List<(Regex, string)>();
                foreach (var raw in lines)
                {
                    var s = raw.Trim();
                    if (s.Length == 0 || s[0] == '#') continue;

                    // "Term: alias, alias" — the colon splits the canonical
                    // spelling from the ways the recognizer writes it.
                    var (term, aliases) = SplitEntry(s);
                    if (term.Length == 0) continue;
                    terms.Add(term);
                    foreach (var alias in aliases)
                        matchers.Add((BuildMatcher(alias, loose: true), term));
                    // The term itself, so a wrong capitalisation still gets fixed.
                    matchers.Add((BuildMatcher(term, loose: false), term));
                }
                _cachedMatchers = matchers;

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

    /// <summary>
    /// Rewrites whole-word occurrences of any alias — and of a term spelled with
    /// the wrong capitals — as the canonical term. Used for the on-device engine,
    /// which has no prompt to bias.
    /// </summary>
    public static string ApplyAliases(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        LoadPrompt();  // picks up a changed file and refreshes the matchers

        List<(Regex rx, string term)> matchers;
        lock (_gate) matchers = _cachedMatchers;
        if (matchers.Count == 0) return text;

        var changes = new List<string>();
        string result = text;
        foreach (var (rx, term) in matchers)
        {
            result = rx.Replace(result, m =>
            {
                // A lower-case term is an ordinary word, not a name: it keeps the
                // capital it happened to have at the start of a sentence.
                string replacement = term;
                if (char.IsUpper(m.Value[0]) && term == term.ToLowerInvariant())
                    replacement = char.ToUpperInvariant(term[0]) + term[1..];
                if (m.Value == replacement) return m.Value;
                changes.Add($"{m.Value} → {replacement}");
                return replacement;
            });
        }
        if (changes.Count > 0) Log.Info($"vocabulary applied: {string.Join(", ", changes)}");
        return result;
    }

    private static (string term, string[] aliases) SplitEntry(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0) return (line.Trim(), Array.Empty<string>());

        var term = line[..colon].Trim();
        var aliases = line[(colon + 1)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (term, aliases);
    }

    /// <summary>
    /// One pattern per alias. Russian inflects borrowed names ("в телеграмме",
    /// "на гитхабе"), so a Cyrillic alias of five letters or more also matches
    /// with up to three trailing letters; shorter ones stay exact to avoid false
    /// hits, and the canonical term is always exact — it is a real word, and a
    /// loose match would eat its longer neighbours.
    /// </summary>
    private static Regex BuildMatcher(string word, bool loose)
    {
        bool cyrillic = word.Any(c => c >= 'Ѐ' && c <= 'ӿ');
        string tail = loose && cyrillic && word.Length >= 5 ? @"\p{L}{0,3}" : "";
        var pattern = $@"\b{Regex.Escape(word)}{tail}\b";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
