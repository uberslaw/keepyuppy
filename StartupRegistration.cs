using Microsoft.Win32;

namespace KeepyUppy;

internal static class StartupRegistration
{
    public const string ValueName = "KeepyUppy";
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string FormatRunCommand(string path) =>
        path.Contains(' ') ? $"\"{path}\"" : path;

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Write("startup read failed: " + ex.Message);
            return false;
        }
    }

    public static bool Enable()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            AppLog.Write("startup write failed: process path is empty");
            return false;
        }

        var command = FormatRunCommand(path);
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
}
