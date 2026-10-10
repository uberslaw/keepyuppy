using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeepyUppy;

public partial class App : Application
{
    private const string MutexName = @"Local\KeepyUppy.Mutex";
    private const string ShowEventName = @"Local\KeepyUppy.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        System.Windows.Forms.Application.EnableVisualStyles();
        AppLog.Write("start " + string.Join(' ', e.Args));

        try
        {
            if (HasFlag(e.Args, "--self-check"))
            {
                var code = SelfCheck.Run();
                Shutdown(code);
                Environment.Exit(code);
                return;
            }

            if (TryGetOption(e.Args, "--preview", out var previewPath))
            {
                ShowPreview(previewPath, awake: false);
                return;
            }

            if (TryGetOption(e.Args, "--preview-on", out var previewOnPath))
            {
                ShowPreview(previewOnPath, awake: true);
                return;
            }

            if (TryGetOption(e.Args, "--preview-timer", out var previewTimerPath))
            {
                ShowPreview(previewTimerPath, awake: false, timer: true);
                return;
            }

            if (!AcquireSingleInstance())
            {
                Shutdown();
                return;
            }

            _window = new MainWindow();
            _window.StartInTray();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex.ToString());
            MessageBox.Show(
                "KeepyUppy could not start. The log is here:\n" + AppLog.FilePath,
                "KeepyUppy",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void ShowPreview(string path, bool awake, bool timer = false)
    {
        var window = new MainWindow(preview: true);
        if (timer)
            window.SetTimerPreview();
        else
            window.SetPreview(awake);
        window.ContentRendered += (_, _) =>
        {
            try
            {
                window.UpdateLayout();
                SavePng(window, path);
                AppLog.Write("preview saved " + path);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex.ToString());
                Shutdown(1);
            }
        };
        window.Show();
    }

    private static void SavePng(Window window, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private bool AcquireSingleInstance()
    {
        bool created;
        try
        {
            _mutex = new Mutex(true, MutexName, out created);
        }
        catch (AbandonedMutexException)
        {
            created = true;
        }

        if (!created)
        {
            SignalExisting();
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var handle = _showEvent;
        var thread = new Thread(() =>
        {
            try
            {
                while (handle.WaitOne())
                    Dispatcher.BeginInvoke(() => _window?.BringUp());
            }
            catch (ObjectDisposedException)
            {
            }
        })
        {
            IsBackground = true,
            Name = "KeepyUppy.Show"
        };
        thread.Start();
        return true;
    }

    private static void SignalExisting()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception.ToString());
        e.Handled = true;
        MessageBox.Show(
            "KeepyUppy hit a problem and kept running. The log is here:\n" + AppLog.FilePath,
            "KeepyUppy",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetOption(string[] args, string name, out string value)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                value = args[i + 1];
                return true;
            }
        }

        value = "";
        return false;
    }
}
