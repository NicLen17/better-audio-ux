using System;
using System.IO;
using Microsoft.Win32;

namespace BetterAudio.Services;

public interface IStartupService
{
    bool Enabled { get; set; }
}

public sealed class StartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("BetterAudio") is string command && command == StartupCommand();
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("BetterAudio", StartupCommand());
            else key.DeleteValue("BetterAudio", false);
        }
    }
    private static string StartupCommand()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "BetterAudio.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("No se encontró BetterAudio.exe.");
        return "\"" + executable + "\" --startup";
    }
}
