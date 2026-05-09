using System.Text;

namespace GroqVoice;

/// <summary>Append-only text log at %APPDATA%\GroqVoice\log.txt with size-based rotation.</summary>
public static class Log
{
    private static readonly object _gate = new();
    private const long MaxBytes = 1_000_000; // ~1 MB then rotate

    public static string Path => System.IO.Path.Combine(Config.Dir, "log.txt");

    public static void Info(string msg)  => Write("INFO", msg);
    public static void Warn(string msg)  => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null)
    {
        if (ex is null) Write("ERR ", msg);
        else Write("ERR ", $"{msg}: {ex.GetType().Name}: {ex.Message}");
    }

    private static void Write(string level, string msg)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Config.Dir);
                Rotate();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}{Environment.NewLine}";
                File.AppendAllText(Path, line, Encoding.UTF8);
            }
        }
        catch { /* logging must never throw */ }
    }

    private static void Rotate()
    {
        try
        {
            var fi = new FileInfo(Path);
            if (!fi.Exists || fi.Length < MaxBytes) return;
            var rolled = System.IO.Path.Combine(Config.Dir, "log.1.txt");
            if (File.Exists(rolled)) File.Delete(rolled);
            File.Move(Path, rolled);
        }
        catch { }
    }
}
