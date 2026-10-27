using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeepyUppy;

public partial class MainWindow : Window
{
    private readonly AwakeService _awake = new();
    private readonly TrayController? _tray;
    private readonly bool _preview;
    private bool _previewOn;
    private TimeSpan? _previewRemaining;
    private bool _allowExit;
    private bool _toldAboutTray;
    private bool _loaded;
    private bool _motionOn;
    private bool _writingBoxes;
    private Storyboard? _bob;
    private System.Drawing.Icon? _windowIcon;
    private int _hours;
    private int _minutes;
    private int _seconds;
    private bool _timerEnabled;
    private bool _startWithWindows;
    private string? _startupError;
    private bool _writingSwitch;

    public MainWindow(bool preview = false)
    {
        _preview = preview;
        InitializeComponent();
        ApplyWindowIcon();

        var settings = SettingsStore.Load();
        _hours = settings.Hours;
        _minutes = settings.Minutes;
        _seconds = settings.Seconds;
        _timerEnabled = settings.TimerEnabled;
        _startWithWindows = settings.StartWithWindows;
        WriteBoxes();

        DataObject.AddPastingHandler(HourBox, TimeBox_Pasting);
        DataObject.AddPastingHandler(MinuteBox, TimeBox_Pasting);
        DataObject.AddPastingHandler(SecondBox, TimeBox_Pasting);

        _awake.StateChanged += () => Dispatcher.Invoke(Sync);
        _awake.Clock += () =>
        {
            UpdateCountdown();
            _tray?.RefreshTooltip();
        };
        _awake.Elapsed += () =>
        {
            Sync();
            _tray?.ShowTip("Time is up", "KeepyUppy turned off. The screen can lock again.");
        };

        Loaded += (_, _) =>
        {
            _loaded = true;
            RestartMotion();
        };

        if (!preview)
        {
            ShowInTaskbar = false;
            _tray = new TrayController(this, _awake);
        }

        if (preview)
            _startWithWindows = StartupRegistration.IsRegistered();
        else
            ReconcileStartup();

        Sync();
        if (!preview)
            SaveSettings();
    }

    public void StartInTray()
    {
        ShowInTaskbar = false;
        AppLog.Write("hidden in tray");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _tray?.ShowTip("KeepyUppy", "Running in the tray. Click the balloon to open.");
        };
        timer.Start();
    }

    public void SetPreview(bool on)
    {
        _previewOn = on;
        _timerEnabled = on;
        _previewRemaining = on ? new TimeSpan(0, 29, 41) : null;
        Sync();
    }

    public void SetTimerPreview()
    {
        _previewOn = false;
        _timerEnabled = true;
        _previewRemaining = null;
        Sync();
    }

    public void BringUp()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    public void ToggleFromUser() => ToggleAwake();

    public void RequestExit()
    {
        _allowExit = true;
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_preview && !_allowExit)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _awake.Dispose();
        _tray?.Dispose();
        _windowIcon?.Dispose();
        base.OnClosed(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && !_preview)
        {
            HideToTray();
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void PowerButton_Click(object sender, RoutedEventArgs e) => ToggleAwake();

    private void HideToTray()
    {
        if (_preview)
            return;

        SaveSettings();
        ShowInTaskbar = false;
        Hide();
        if (!_toldAboutTray)
        {
            _toldAboutTray = true;
            _tray?.ShowTip("Still running", "KeepyUppy stays in the tray. Right-click the balloon and choose Exit to quit.");
        }
    }

    private void ToggleAwake()
    {
        if (_preview)
            return;

        if (_awake.IsRunning)
        {
            _awake.Stop("user");
            return;
        }

        ReadBoxes();
        WriteBoxes();
        SaveSettings();
        _awake.Start(SelectedDuration());
    }

    private void TimerSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_writingSwitch)
            return;

        _timerEnabled = TimerSwitch.IsChecked == true;
        SaveSettings();
        if (_awake.IsRunning)
            _awake.SetTurnOffAfter(SelectedDuration());
        else
            UpdateCountdown();
    }

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_writingSwitch || _preview)
        {
            if (_preview)
                StartupSwitch.IsChecked = _startWithWindows;
            return;
        }

        var wantOn = StartupSwitch.IsChecked == true;
        var ok = wantOn ? StartupRegistration.Enable() : StartupRegistration.Disable();
        _startWithWindows = StartupRegistration.IsRegistered();
        if (!ok || _startWithWindows != wantOn)
            _startupError = wantOn ? "Couldn't start with Windows." : "Couldn't turn off start with Windows.";
        else
            _startupError = null;

        Sync();
        SaveSettings();
    }

    private void ReconcileStartup()
    {
        var registered = StartupRegistration.IsRegistered();
        if (_startWithWindows && !registered)
        {
            var wrote = StartupRegistration.Enable();
            registered = StartupRegistration.IsRegistered();
            if (!wrote || !registered)
                _startupError = "Couldn't start with Windows.";
        }
        else if (!_startWithWindows && registered)
        {
            AppLog.Write("startup registry present; preference updated");
        }

        _startWithWindows = registered;
    }

    private TimeSpan SelectedDuration() =>
        _timerEnabled ? new TimeSpan(_hours, _minutes, _seconds) : TimeSpan.Zero;

    private void Sync()
    {
        var on = _previewOn || _awake.IsRunning;
        if (on)
        {
            StatusText.Text = "On";
            StatusPill.Background = BrushOf("GlassBlueBrush");
            PowerButton.Content = "Turn off";
            PowerButton.Background = BrushOf("GlassCoralBrush");
            PowerButton.Foreground = BrushOf("CoralInkBrush");
        }
        else
        {
            StatusText.Text = "Off";
            StatusPill.Background = BrushOf("GlassNeutralBrush");
            PowerButton.Content = "Turn on";
            PowerButton.Background = BrushOf("GlassBlueBrush");
            PowerButton.Foreground = BrushOf("InkBrush");
        }

        _writingSwitch = true;
        TimerSwitch.IsChecked = _timerEnabled;
        StartupSwitch.IsChecked = _startWithWindows;
        _writingSwitch = false;
        UpdateCountdown();
        SetMotion(on);
        _tray?.Sync(on);

        var error = _startupError ?? _awake.LastError;
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateCountdown()
    {
        TimePanel.Visibility = _timerEnabled ? Visibility.Visible : Visibility.Collapsed;

        TimeSpan? remaining = null;
        if (_previewOn)
            remaining = _previewRemaining;
        else if (_awake.IsRunning && _timerEnabled)
            remaining = _awake.Remaining;

        var counting = _timerEnabled && remaining is not null;
        SpinnerGrid.Visibility = _timerEnabled && !counting ? Visibility.Visible : Visibility.Collapsed;
        CountdownCaption.Visibility = counting ? Visibility.Visible : Visibility.Collapsed;
        CountdownText.Visibility = counting ? Visibility.Visible : Visibility.Collapsed;

        if (remaining is TimeSpan left)
            ShowCountdown("Turns off in", TimeText.Hms(left), 32, "BingoBrush");
    }

    private void ShowCountdown(string caption, string value, double size, string brushKey)
    {
        CountdownCaption.Text = caption;
        CountdownText.Text = value;
        CountdownText.FontSize = size;
        CountdownText.Foreground = BrushOf(brushKey);
    }

    private void StepUp_Click(object sender, RoutedEventArgs e) => Step(sender, 1);

    private void StepDown_Click(object sender, RoutedEventArgs e) => Step(sender, -1);

    private void Step(object sender, int direction)
    {
        if (sender is not FrameworkElement element || element.Tag is not string part)
            return;

        ReadBoxes();
        switch (part)
        {
            case "h":
                _hours = Math.Clamp(_hours + direction, 0, 99);
                break;
            case "m":
                _minutes = Wrap(_minutes + direction, 59);
                break;
            case "s":
                _seconds = Wrap(_seconds + direction, 59);
                break;
        }

        WriteBoxes();
        SaveSettings();
        UpdateCountdown();
    }

    private void TimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
    }

    private void TimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            Step(sender, 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Step(sender, -1);
            e.Handled = true;
        }
    }

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_writingBoxes)
            return;

        ReadBoxes();
        WriteBoxes();
        SaveSettings();
        UpdateCountdown();
    }

    private void TimeBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            box.Dispatcher.BeginInvoke(() => box.SelectAll());
    }

    private void TimeBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)) || e.DataObject.GetData(typeof(string)) is not string text)
        {
            e.CancelCommand();
            return;
        }

        if (text.Any(ch => !char.IsDigit(ch)))
            e.CancelCommand();
    }

    private void ShellContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        var radius = Shell.CornerRadius.TopLeft;
        if (ShellContent.Clip is not RectangleGeometry clip)
        {
            clip = new RectangleGeometry();
            ShellContent.Clip = clip;
        }

        clip.RadiusX = radius;
        clip.RadiusY = radius;
        clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }

    private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        if (IsInteractive(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ReadBoxes()
    {
        _hours = Parse(HourBox.Text, 0, 99);
        _minutes = Parse(MinuteBox.Text, 0, 59);
        _seconds = Parse(SecondBox.Text, 0, 59);
    }

    private void WriteBoxes()
    {
        _writingBoxes = true;
        HourBox.Text = _hours.ToString("00");
        MinuteBox.Text = _minutes.ToString("00");
        SecondBox.Text = _seconds.ToString("00");
        _writingBoxes = false;
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(new AppSettings(_hours, _minutes, _seconds, _timerEnabled, _startWithWindows));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex.ToString());
            ErrorText.Text = "Couldn't save settings.";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void SetMotion(bool on)
    {
        if (_motionOn == on)
        {
            if (_loaded && on && _bob == null)
                RestartMotion();
            return;
        }

        _motionOn = on;
        if (_loaded)
            RestartMotion();
    }

    private void RestartMotion()
    {
        if (_bob != null)
        {
            _bob.Stop(this);
            _bob.Remove(this);
            _bob = null;
        }

        if (!_motionOn)
            return;

        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        AddBob(storyboard, ShiftOf(BlueBalloon), TimeSpan.Zero, -10, 2.6);
        AddBob(storyboard, ShiftOf(OrangeBalloon), TimeSpan.FromSeconds(0.35), -8, 3.0);
        AddBob(storyboard, ShiftOf(GreenBalloon), TimeSpan.FromSeconds(0.15), -7, 3.3);
        storyboard.Begin(this, isControllable: true);
        _bob = storyboard;
    }

    private void ApplyWindowIcon()
    {
        try
        {
            _windowIcon = BalloonIcons.Create(true, 64);
            Icon = Imaging.CreateBitmapSourceFromHIcon(
                _windowIcon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex)
        {
            AppLog.Write("window icon: " + ex.Message);
        }
    }

    private static TranslateTransform ShiftOf(UIElement element)
    {
        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform shift)
                    return shift;
            }
        }

        var created = new TranslateTransform();
        var existing = element.RenderTransform;
        element.RenderTransform = new TransformGroup
        {
            Children =
            {
                existing ?? Transform.Identity,
                created
            }
        };
        return created;
    }

    private static void AddBob(Storyboard storyboard, TranslateTransform target, TimeSpan begin, double distance, double seconds)
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = distance,
            Duration = TimeSpan.FromSeconds(seconds),
            BeginTime = begin,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(animation);
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase or TextBox)
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static int Parse(string? text, int min, int max)
    {
        if (!int.TryParse(text, out var value))
            return min;

        return Math.Clamp(value, min, max);
    }

    private static int Wrap(int value, int max)
    {
        if (value < 0)
            return max;
        if (value > max)
            return 0;
        return value;
    }

    private static Brush BrushOf(string key) => (Brush)System.Windows.Application.Current.FindResource(key);
}
