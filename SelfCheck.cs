using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KeepyUppy;

internal static class SelfCheck
{
    public static int Run()
    {
        var lines = new List<string>();
        var ok = true;
        try
        {
            var expectedSize = Environment.Is64BitProcess ? 40 : 28;
            lines.Add($"inputSize={NativeMethods.InputSize}");
            if (NativeMethods.InputSize != expectedSize)
            {
                ok = false;
                lines.Add($"FAIL input size, expected {expectedSize}");
            }

            var nudged = NativeMethods.NudgeMouse();
            lines.Add($"nudge={nudged}");
            if (!nudged)
                ok = false;

            Thread.Sleep(40);
            var idle = NativeMethods.GetIdleTime();
            lines.Add($"idleMs={(int)idle.TotalMilliseconds}");
            if (idle > TimeSpan.FromSeconds(2))
            {
                ok = false;
                lines.Add("FAIL idle timer did not reset");
            }

            var execution = NativeMethods.SetAwakeExecutionState();
            lines.Add($"executionState={execution}");
            if (!execution)
                ok = false;

            using (var icon = BalloonIcons.Create(true))
            {
                lines.Add($"icon={icon.Width}x{icon.Height}");
                if (icon.Handle == IntPtr.Zero)
                {
                    ok = false;
                    lines.Add("FAIL icon");
                }
            }

            var folder = Path.Combine(Path.GetTempPath(), "KeepyUppy-selfcheck-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var settings in new AppSettings[]
                {
                    new(1, 2, 3, true, true, false),
                    new(4, 5, 6, false, false, true)
                })
                {
                    SettingsStore.Save(settings, folder);
                    var loaded = SettingsStore.Load(folder);
                    lines.Add($"settings={loaded.Hours:00}:{loaded.Minutes:00}:{loaded.Seconds:00} timer={loaded.TimerEnabled} startup={loaded.StartWithWindows} launch={loaded.LaunchAtLogin}");
                    if (loaded != settings)
                    {
                        ok = false;
                        lines.Add("FAIL settings read-back");
                    }
                }

                File.WriteAllText(Path.Combine(folder, "settings.json"), """
                    {
                      "hours": 4,
                      "minutes": 5,
                      "seconds": 6,
                      "timerEnabled": true
                    }
                    """);
                var partial = SettingsStore.Load(folder);
                if (partial.StartWithWindows || partial.LaunchAtLogin || !partial.TimerEnabled || partial.Hours != 4 || partial.Minutes != 5 || partial.Seconds != 6)
                {
                    ok = false;
                    lines.Add("FAIL settings missing startup field");
                }
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }

            var plain = StartupRegistration.FormatRunCommand(@"C:\NoSpaces\KeepyUppy.exe");
            var plainAwake = StartupRegistration.FormatRunCommand(@"C:\NoSpaces\KeepyUppy.exe", awake: true);
            var quoted = StartupRegistration.FormatRunCommand(@"C:\Program Files\KeepyUppy.exe");
            var quotedAwake = StartupRegistration.FormatRunCommand(@"C:\Program Files\KeepyUppy.exe", awake: true);
            if (plain != @"C:\NoSpaces\KeepyUppy.exe"
                || quoted != @"""C:\Program Files\KeepyUppy.exe"""
                || plainAwake != @"C:\NoSpaces\KeepyUppy.exe --awake"
                || quotedAwake != @"""C:\Program Files\KeepyUppy.exe"" --awake"
                || StartupRegistration.HasAwakeArgument(plain)
                || StartupRegistration.HasAwakeArgument(quoted)
                || StartupRegistration.HasAwakeArgument(@"C:\awake\KeepyUppy.exe")
                || !StartupRegistration.HasAwakeArgument(plainAwake)
                || !StartupRegistration.HasAwakeArgument(quotedAwake))
            {
                ok = false;
                lines.Add("FAIL startup command format");
            }

            CheckStartupRoundTrip(lines, ref ok);

            if (TimeText.Hms(new TimeSpan(1, 2, 3)) != "01:02:03")
            {
                ok = false;
                lines.Add("FAIL time format");
            }
        }
        catch (Exception ex)
        {
            ok = false;
            lines.Add("FAIL " + ex.Message);
            AppLog.Write(ex.ToString());
        }
        finally
        {
            NativeMethods.ClearExecutionState();
        }

        lines.Insert(0, ok ? "PASS" : "FAIL");
        var report = string.Join(Environment.NewLine, lines);
        AppLog.Write(report);
        try
        {
            Directory.CreateDirectory(AppLog.Folder);
            File.WriteAllText(Path.Combine(AppLog.Folder, "self-check.txt"), report);
        }
        catch (Exception ex)
        {
            AppLog.Write("self-check file: " + ex.Message);
        }

        WriteConsole(report);
        return ok ? 0 : 1;
    }

    private static void CheckStartupRoundTrip(List<string> lines, ref bool ok)
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        object? original = null;
        var originalKind = RegistryValueKind.String;
        var hadOriginal = false;
        string[] othersBefore;
        using (var key = Registry.CurrentUser.OpenSubKey(runKey, writable: false))
        {
            othersBefore = key?.GetValueNames().Where(name => !name.Equals(StartupRegistration.ValueName, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            if (key?.GetValue(StartupRegistration.ValueName) is object value)
            {
                hadOriginal = true;
                original = value;
                originalKind = key.GetValueKind(StartupRegistration.ValueName);
            }
        }

        try
        {
            CheckEnabledMode(lines, ref ok, awake: true, StartupMode.StartWithWindows, "awake", runKey, othersBefore);
            CheckEnabledMode(lines, ref ok, awake: false, StartupMode.LaunchAtLogin, "login", runKey, othersBefore);

            if (!StartupRegistration.Disable() || !StartupRegistration.TryGetMode(out var off) || off != StartupMode.Off)
            {
                ok = false;
                lines.Add("FAIL startup disable");
            }
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            if (key is not null)
            {
                if (!hadOriginal)
                    key.DeleteValue(StartupRegistration.ValueName, throwOnMissingValue: false);
                else
                    key.SetValue(StartupRegistration.ValueName, original!, originalKind);
            }
        }
    }

    private static void CheckEnabledMode(List<string> lines, ref bool ok, bool awake, StartupMode expected, string label, string runKey, string[] othersBefore)
    {
        if (!StartupRegistration.Enable(awake) || !StartupRegistration.TryGetMode(out var mode) || mode != expected)
        {
            ok = false;
            lines.Add("FAIL startup enable " + label);
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: false);
        var value = key?.GetValue(StartupRegistration.ValueName) as string;
        var path = Environment.ProcessPath ?? "";
        var expectedCommand = StartupRegistration.FormatRunCommand(path, awake);
        lines.Add("startup-" + label + "=" + value);
        if (!string.Equals(value, expectedCommand, StringComparison.OrdinalIgnoreCase)
            || StartupRegistration.HasAwakeArgument(value ?? "") != awake)
        {
            ok = false;
            lines.Add("FAIL startup command " + label);
        }

        var names = key?.GetValueNames() ?? [];
        if (names.Count(name => name.Equals(StartupRegistration.ValueName, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            ok = false;
            lines.Add("FAIL startup value count " + label);
        }

        var others = names.Where(name => !name.Equals(StartupRegistration.ValueName, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!othersBefore.SequenceEqual(others, StringComparer.OrdinalIgnoreCase))
        {
            ok = false;
            lines.Add("FAIL startup touched other run values");
        }
    }

    private static void WriteConsole(string report)
    {
        try
        {
            if (!AttachConsole(-1))
                return;

            using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(writer);
            Console.WriteLine(report);
        }
        catch
        {
            // Self-check still returns an exit code when no console is attached.
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
