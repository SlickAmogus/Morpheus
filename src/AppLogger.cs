using System;
using System.IO;

namespace Morpheus;

public static class AppLogger
{
    private static string _logPath = "";

    public static string LogPath => _logPath;

    public static void Initialize(string baseDir)
    {
        var folderName = Path.GetFileName(Path.GetFullPath(baseDir).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var logsDir = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(logsDir);
        _logPath = Path.Combine(logsDir, $"Morpheus_{folderName}_{timestamp}.txt");
        Log("Morpheus started");
    }

    public static void Log(string message)
    {
        if (string.IsNullOrEmpty(_logPath)) return;
        try
        {
            File.AppendAllText(_logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
