namespace VintageModUpdater.Core;

internal sealed class UpdateOperationLog : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    private UpdateOperationLog(string logPath, StreamWriter writer)
    {
        LogPath = logPath;
        _writer = writer;
    }

    public string LogPath { get; }

    public static UpdateOperationLog Create(string modsPath, string modId)
    {
        var safeModsPath = PathGuard.NormalizePath(modsPath);
        var logDirectory = Path.Combine(safeModsPath, ".vintage-mod-updater", "logs");
        Directory.CreateDirectory(logDirectory);

        var fileName = $"update-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Sanitize(modId)}.log";
        var logPath = Path.Combine(logDirectory, fileName);
        var writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
        writer.WriteLine($"Vintage Mod Updater update log");
        writer.WriteLine($"Started: {DateTimeOffset.Now:O}");
        writer.WriteLine($"Mod: {modId}");
        writer.WriteLine();

        return new UpdateOperationLog(logPath, writer);
    }

    public void WriteStep(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        _writer.WriteLine(line);
    }

    public void WriteFailure(Exception exception)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writer.WriteLine();
        _writer.WriteLine($"Failed: {DateTimeOffset.Now:O}");
        _writer.WriteLine(exception.ToString());
    }

    public void WriteSuccess(string destinationPath, string? installedVersion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writer.WriteLine();
        _writer.WriteLine($"Completed: {DateTimeOffset.Now:O}");
        _writer.WriteLine($"Installed to: {destinationPath}");
        _writer.WriteLine($"Archive version: {installedVersion ?? "unknown"}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Dispose();
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "mod" : sanitized;
    }
}
