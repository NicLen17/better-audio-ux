using System;
using System.IO;

namespace BetterAudio.Services;

internal static class Log
{
    private static readonly object Gate = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterAudio");
    public static void Write(string message, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message} {error}\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
