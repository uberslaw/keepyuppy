namespace KeepyUppy;

internal static class AppLog
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeepyUppy");

    public static string FilePath => Path.Combine(Folder, "keepyuppy.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // A log failure must not take down the tray icon.
        }
    }
}
