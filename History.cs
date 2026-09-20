using System.Text.Json;
using System.Text.Json.Serialization;

namespace GroqVoice;

/// <summary>What produced an entry — shown as a tag in the history window.</summary>
public enum TakeKind { Dictation, Task, Edit }

public sealed class HistoryEntry
{
    [JsonPropertyName("time")] public DateTime Time { get; set; } = DateTime.Now;
    [JsonPropertyName("kind")] public string Kind { get; set; } = nameof(TakeKind.Dictation);
    [JsonPropertyName("text")] public string Text { get; set; } = "";

    /// <summary>
    /// What went into the language model — the speech, or the selection it
    /// replaced — when <see cref="Text"/> is the model's answer. A rewrite that
    /// came out wrong then never costs the original.
    /// </summary>
    [JsonPropertyName("source")] public string? Source { get; set; }

    /// <summary>Single-line, shortened form for menus and list rows.</summary>
    public string OneLine(int max = 60)
    {
        var s = Text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}

/// <summary>
/// Keeps the last N results in %APPDATA%\GroqVoice\history.jsonl. One JSON object
/// per line, so a truncated write costs one entry rather than the file, and the
/// file stays readable with a text editor.
/// </summary>
public sealed class History
{
    public static string Path => System.IO.Path.Combine(Config.Dir, "history.jsonl");

    private readonly object _gate = new();
    private readonly List<HistoryEntry> _entries = new();
    private int _limit;

    /// <summary>Raised after any change, on whatever thread made it.</summary>
    public event Action? Changed;

    public History(int limit)
    {
        _limit = Math.Max(0, limit);
        Load();
    }

    public int Limit
    {
        get => _limit;
        set
        {
            _limit = Math.Max(0, value);
            lock (_gate) { if (Trim()) Save(); }
            Changed?.Invoke();
        }
    }

    /// <summary>Newest last.</summary>
    public IReadOnlyList<HistoryEntry> Entries { get { lock (_gate) return _entries.ToArray(); } }

    public HistoryEntry? Latest { get { lock (_gate) return _entries.Count > 0 ? _entries[^1] : null; } }

    public void Add(string text, TakeKind kind, string? source = null)
    {
        if (_limit == 0) return;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;

        var original = source?.Trim();
        lock (_gate)
        {
            _entries.Add(new HistoryEntry
            {
                Time = DateTime.Now,
                Kind = kind.ToString(),
                Text = trimmed,
                Source = string.IsNullOrEmpty(original) || original == trimmed ? null : original,
            });
            Trim();
            Save();
        }
        Changed?.Invoke();
    }

    public void Remove(HistoryEntry entry)
    {
        lock (_gate) { if (!_entries.Remove(entry)) return; Save(); }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception ex) { Log.Warn($"history clear failed: {ex.Message}"); }
        }
        Changed?.Invoke();
    }

    /// <summary>Drops the oldest entries past the limit. Call under the lock.</summary>
    private bool Trim()
    {
        if (_entries.Count <= _limit) return false;
        _entries.RemoveRange(0, _entries.Count - _limit);
        return true;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(Path)) return;
            foreach (var line in File.ReadAllLines(Path))
            {
                if (line.Length == 0) continue;
                // A line written by another version, or a half-written one, is skipped.
                try
                {
                    var e = JsonSerializer.Deserialize<HistoryEntry>(line);
                    if (e != null && e.Text.Length > 0) _entries.Add(e);
                }
                catch { }
            }
            Trim();
        }
        catch (Exception ex) { Log.Warn($"history load failed: {ex.Message}"); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            var lines = _entries.Select(e => JsonSerializer.Serialize(e));
            File.WriteAllLines(Path, lines);
        }
        catch (Exception ex) { Log.Warn($"history save failed: {ex.Message}"); }
    }
}
