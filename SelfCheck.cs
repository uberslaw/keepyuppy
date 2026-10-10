using System.Runtime.InteropServices;

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
                var settings = new AppSettings(1, 2, 3);
                SettingsStore.Save(settings, folder);
                var loaded = SettingsStore.Load(folder);
                lines.Add($"settings={loaded.Hours:00}:{loaded.Minutes:00}:{loaded.Seconds:00}");
                if (loaded != settings)
                {
                    ok = false;
                    lines.Add("FAIL settings read-back");
                }
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }

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
