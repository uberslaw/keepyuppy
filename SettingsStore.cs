using System.Text.Json;

namespace KeepyUppy;

internal readonly record struct AppSettings(int Hours, int Minutes, int Seconds, bool TimerEnabled = false)
{
    public static AppSettings Default => new(0, 30, 0);

    public AppSettings Clamped() => new(
        Math.Clamp(Hours, 0, 99),
        Math.Clamp(Minutes, 0, 59),
        Math.Clamp(Seconds, 0, 59),
        TimerEnabled);

    public TimeSpan ToDuration()
    {
        var clamped = Clamped();
        return new TimeSpan(clamped.Hours, clamped.Minutes, clamped.Seconds);
    }
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Folder { get; } = AppLog.Folder;

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load(string? folder = null)
    {
        var path = Path.Combine(folder ?? Folder, "settings.json");
        try
        {
            if (!File.Exists(path))
                return AppSettings.Default;

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            return loaded.Clamped();
        }
        catch (Exception ex)
        {
            AppLog.Write("settings load failed: " + ex.Message);
            return AppSettings.Default;
        }
    }

    public static void Save(AppSettings settings, string? folder = null)
    {
        var dir = folder ?? Folder;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        var clamped = settings.Clamped();
        var json = JsonSerializer.Serialize(clamped, Options);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);

        var readBack = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(temp), Options);
        if (readBack != clamped)
            throw new InvalidDataException("Settings read-back did not match what was written.");

        File.Move(temp, path, overwrite: true);

        var landed = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
        if (landed != clamped)
            throw new InvalidDataException("Settings on disk did not match what was written.");
    }
}
