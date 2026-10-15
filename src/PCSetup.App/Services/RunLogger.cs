using System.Text.RegularExpressions;

namespace PCSetup.Services;

public sealed class RunLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public RunLogger()
    {
        _writer = new StreamWriter(Stream.Null) { AutoFlush = true };
        Path = "";
    }

    public RunLogger(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true
        };
        Path = path;
    }

    public string Path { get; }
    public event Action<string>? LineWritten;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    public void Write(string level, string message)
    {
        var safe = Redact(message);
        var line = $"{DateTimeOffset.Now:O} [{level}] {safe}";
        lock (_gate)
        {
            _writer.WriteLine(line);
        }
        LineWritten?.Invoke(line);
    }

    private static string Redact(string value)
    {
        value = Regex.Replace(value, @"(?i)(password|passwd|token|secret|apikey|api_key)\s*[=:]\s*[^\s;&]+", "$1=<redacted>");
        value = Regex.Replace(value, @"(?i)([?&](?:token|key|sig|signature|code)=)[^&\s]+", "$1<redacted>");
        return value;
    }

    public void Dispose() => _writer.Dispose();
}
