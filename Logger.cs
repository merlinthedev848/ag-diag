using System;
using System.IO;
using System.Text.Json;

public static class Logger
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgilicoToolkit", "activity_log.jsonl");
    private static readonly object Sync = new();

    static Logger()
    {
        try
        {
            string? dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }

    public static void Log(string message) => Log("Info", message);

    public static void Log(string level, string message, string? operation = null, Exception? exception = null)
    {
        try
        {
            var entry = new
            {
                timestampUtc = DateTime.UtcNow,
                level = string.IsNullOrWhiteSpace(level) ? "Info" : level,
                operation,
                message = Redact(message),
                exception = exception == null ? null : new
                {
                    type = exception.GetType().FullName,
                    message = Redact(exception.Message)
                }
            };

            string json = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (Sync)
            {
                File.AppendAllText(LogFile, json);
            }
        }
        catch
        {
            // Logging is best-effort and must not mask the original operation failure.
        }
    }

    public static void Error(string message, Exception? exception = null, string? operation = null) =>
        Log("Error", message, operation, exception);

    public static void Warning(string message, string? operation = null) =>
        Log("Warning", message, operation);

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Avoid accidentally persisting obvious bearer credentials/secrets.
        string[] secretMarkers = { "ClientToken", "access_token", "refresh_token", "password", "Authorization:" };
        string result = value;
        foreach (string marker in secretMarkers)
        {
            int start = 0;
            while ((start = result.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int separator = result.IndexOfAny(new[] { '=', ':' }, start + marker.Length);
                if (separator < 0) break;
                int end = result.IndexOfAny(new[] { ' ', ';', ',', '\r', '\n' }, separator + 1);
                if (end < 0) end = result.Length;
                result = result.Remove(separator + 1, end - separator - 1).Insert(separator + 1, "[REDACTED]");
                start = separator + 1;
            }
        }
        return result;
    }
}
