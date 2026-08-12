using System;
using System.IO;

public static class Logger
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgilicoToolkit", "activity_log.txt");
    private static readonly object _lock = new();
    static Logger()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile);
            if (dir != null) Directory.CreateDirectory(dir);
        }
        catch { }
    }
    public static void Log(string message)
    {
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                File.AppendAllText(LogFile, entry);
            }
        }
        catch { }
    }
}
