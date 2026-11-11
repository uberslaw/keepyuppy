using System.Text;
using Microsoft.Win32;

namespace KeepyUppy;

internal enum StartupMode
{
    Off,
    LaunchAtLogin,
    StartWithWindows
}

internal static class StartupRegistration
{
    public const string ValueName = "KeepyUppy";
    public const string AwakeArgument = "--awake";
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string FormatRunCommand(string path, bool awake = false)
    {
        var command = path.Contains(' ') ? $"\"{path}\"" : path;
        return awake ? command + " " + AwakeArgument : command;
    }

    public static bool HasAwakeArgument(string command)
    {
        var skippedPath = false;
        foreach (var token in Tokenize(command))
        {
            if (!skippedPath)
            {
                skippedPath = true;
                continue;
            }

            if (token.Equals(AwakeArgument, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool TryGetMode(out StartupMode mode)
    {
        mode = StartupMode.Off;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
            if (key?.GetValue(ValueName) is not object raw)
                return true;

            var text = raw as string ?? "";
            mode = HasAwakeArgument(text) ? StartupMode.StartWithWindows : StartupMode.LaunchAtLogin;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("startup read failed: " + ex.Message);
            return false;
        }
    }

    public static bool Enable(bool awake)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            AppLog.Write("startup write failed: process path is empty");
            return false;
        }

        var command = FormatRunCommand(path, awake);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
            AppLog.Write("startup on " + command);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("startup write failed: " + ex.Message);
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            AppLog.Write("startup off");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("startup remove failed: " + ex.Message);
            return false;
        }
    }

    private static IEnumerable<string> Tokenize(string command)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in command)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }
}
