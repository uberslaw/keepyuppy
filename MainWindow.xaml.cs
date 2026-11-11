using System.ComponentModel;
using System.Reflection;
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
    private bool _launchAtLogin;
    private string? _startupError;
    private bool _writingSwitch;
    private bool _parked;
    private bool _hasHome;
    private double _homeLeft;
    private double _homeTop;

    private static readonly FieldInfo? VisualParentField =
        typeof(Visual).GetField("_parent", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? IsRootElementProperty =
        typeof(Visual).GetProperty("IsRootElement", BindingFlags.Instance | BindingFlags.NonPublic);

    public MainWindow(bool preview = false)
    {
        _preview = preview;
        InitializeComponent();
        if (!preview)
        {
            // Park before any property that can create an HWND, so login never flashes the window.
            ShowActivated = false;
            ParkHidden();
        }

        ApplyWindowIcon();

        var settings = SettingsStore.Load();
        _hours = settings.Hours;
        _minutes = settings.Minutes;
        _seconds = settings.Seconds;
        _timerEnabled = settings.TimerEnabled;
        _startWithWindows = settings.StartWithWindows;
        _launchAtLogin = settings.LaunchAtLogin;
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
            _tray = new TrayController(this, _awake);

        if (preview)
        {
            if (StartupRegistration.TryGetMode(out var mode))
                ApplyStartupMode(mode);
        }
        else
        {
            ReconcileStartup();
        }

        Sync();
        if (!preview)
            SaveSettings();
    }

    public void StartInTray(bool armAwake = false)
    {
        EnsureHiddenSource();
        AppLog.Write("hidden in tray");
        if (armAwake)
            ArmKeepAwakeAtStartup();

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
        TimePanel.Visibility = Visibility.Visible;
        SpinnerGrid.Visibility = Visibility.Visible;
        SpinnerGrid.IsEnabled = true;
        SpinnerGrid.Opacity = 1;
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
        ShowWindow();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        if (_parked)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
                UpdateLayout();

            if (_hasHome)
            {
                Left = _homeLeft;
                Top = _homeTop;
            }
            else
                CenterInWorkArea();

            _parked = false;
        }

        ShowActivated = true;
        ShowInTaskbar = true;
        Opacity = 1;
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
        // Hide() destroys the layered HWND and leaves the root visual parented, so the next Show() throws.
        ParkHidden();
        if (!_toldAboutTray)
        {
            _toldAboutTray = true;
            _tray?.ShowTip("Still running", "KeepyUppy stays in the tray. Right-click the balloon and choose Exit to quit.");
        }
    }

    internal int RunShowTest()
    {
        try
        {
            EnsureHiddenSource();
            if (!IsHiddenFromUser())
                throw new InvalidOperationException("hidden launch was visible");

            for (var i = 0; i < 2; i++)
            {
                HideToTray();
                if (PresentationSource.FromVisual(this) is not HwndSource kept || kept.IsDisposed || kept.Handle == IntPtr.Zero)
                    throw new InvalidOperationException("HideToTray destroyed the window source.");

                BringUp();
                if (!IsPresented())
                    throw new InvalidOperationException("BringUp did not present the window.");
            }

            ProveStaleRootDoesNotEscape();

            Hide();
            BringUp();
            if (!IsPresented())
                throw new InvalidOperationException("Show after Hide did not present the window.");

            HideToTray();
            if (!IsHiddenFromUser())
                throw new InvalidOperationException("window left on screen");

            AppLog.Write("show-test ok");
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Write("show-test FAIL " + ex);
            return 1;
        }
        finally
        {
            try
            {
                _allowExit = true;
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Write("show-test close: " + ex.Message);
            }
        }
    }

    private void EnsureHiddenSource()
    {
        ParkHidden();
        ShowWithoutStealingFocus();
    }

    private void ShowWithoutStealingFocus()
    {
        ShowActivated = false;
        try
        {
            ShowWindow();
        }
        finally
        {
            ShowActivated = true;
        }
    }

    private void ParkHidden()
    {
        RememberHomeIfOnScreen();
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;
        MoveOffScreen();
        ShowInTaskbar = false;
        _parked = true;
    }

    private void RememberHomeIfOnScreen()
    {
        if (_parked || !IsVisible || Opacity <= 0 || !IntersectsVirtualScreen())
            return;

        _homeLeft = Left;
        _homeTop = Top;
        _hasHome = true;
    }

    private void MoveOffScreen()
    {
        var width = WindowWidth();
        var height = WindowHeight();
        Left = SystemParameters.VirtualScreenLeft - width - 80;
        Top = SystemParameters.VirtualScreenTop - height - 80;
    }

    private void CenterInWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - WindowWidth()) / 2);
        Top = area.Top + Math.Max(0, (area.Height - WindowHeight()) / 2);
    }

    private bool IsHiddenFromUser() =>
        Opacity <= 0.01 && !ShowInTaskbar && !IntersectsVirtualScreen();

    private bool IsPresented() =>
        Opacity >= 0.99 && ShowInTaskbar && WindowState != WindowState.Minimized && IntersectsVirtualScreen();

    private bool IntersectsVirtualScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        return Left < right && Left + WindowWidth() > left && Top < bottom && Top + WindowHeight() > top;
    }

    private double WindowWidth()
    {
        if (ActualWidth > 0)
            return ActualWidth;
        return double.IsNaN(Width) || Width <= 0 ? 430 : Width;
    }

    private double WindowHeight()
    {
        if (ActualHeight > 0)
            return ActualHeight;
        return double.IsNaN(Height) || Height <= 0 ? 700 : Height;
    }

    // Show() throws ArgumentException when this visual is still a composition root after its target dropped it.
    private void ShowWindow()
    {
        try
        {
            PrepareRootForShow();
            Show();
        }
        catch (ArgumentException ex) when (RootVisualBlockedShow(ex))
        {
            AppLog.Write("show recovered: " + ex.Message);
            PrepareRootForShow();
            Show();
        }

        if (!IsRootAttached())
            EnsureRootAttached();
    }

    private void ProveStaleRootDoesNotEscape()
    {
        Hide();
        var rootProperty = IsRootElementProperty ?? throw new InvalidOperationException("IsRootElement missing");
        var createdField = typeof(Window).GetField("_hwndCreatedButNotShown", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("show state field missing");

        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is not HwndSource source || source.IsDisposed)
            throw new InvalidOperationException("expected a live window source");

        // The crash is Show() assigning this visual to a target while it is still marked as a composition root.
        source.RootVisual = null;
        rootProperty.SetValue(this, true);
        createdField.SetValue(this, true);

        var threw = false;
        try
        {
            Show();
        }
        catch (ArgumentException ex) when (IsRootVisualParentMessage(ex))
        {
            threw = true;
        }

        if (!threw)
            throw new InvalidOperationException("Show did not throw ArgumentException for a stale root visual");

        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is HwndSource after && after.RootVisual != null)
            after.RootVisual = null;
        rootProperty.SetValue(this, true);
        createdField.SetValue(this, true);
        ShowWindow();

        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is not HwndSource restored
            || restored.IsDisposed
            || !ReferenceEquals(restored.RootVisual, this))
            throw new InvalidOperationException("Show after the stale-root failure did not restore the window");
    }

    private void PrepareRootForShow()
    {
        if (!ShowWouldFailBecauseRootHasParent())
            return;

        ReleaseRootVisual(force: true);
        EnsureRootAttached();
    }

    private bool ShowWouldFailBecauseRootHasParent()
    {
        if (!VisualHasParent() && !IsMarkedRootElement())
            return false;

        var source = SourceForThis();
        if (source == null || source.IsDisposed || source.Handle == IntPtr.Zero)
            return true;

        return !ReferenceEquals(source.RootVisual, this);
    }

    private bool RootVisualBlockedShow(ArgumentException ex) =>
        IsRootVisualParentMessage(ex) || VisualHasParent() || IsMarkedRootElement();

    private static bool IsRootVisualParentMessage(ArgumentException ex) =>
        ex.Message.Contains("VisualTarget", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("root Visual", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("cannot have a parent", StringComparison.OrdinalIgnoreCase);

    private void ReleaseRootVisual(bool force = false)
    {
        var source = SourceForThis();
        if (!force && source != null && !source.IsDisposed && source.Handle != IntPtr.Zero && ReferenceEquals(source.RootVisual, this))
            return;

        if (source != null)
        {
            try
            {
                if (source.RootVisual != null)
                    source.RootVisual = null;
            }
            catch (Exception ex)
            {
                AppLog.Write("root visual detach: " + ex.Message);
            }
        }

        if (source == null || source.IsDisposed || !ReferenceEquals(source.RootVisual, this))
            ClearStaleRootState();
    }

    private void EnsureRootAttached()
    {
        if (IsRootAttached())
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || HwndSource.FromHwnd(handle) is not HwndSource source || source.IsDisposed)
            return;

        ClearStaleRootState();
        try
        {
            if (!ReferenceEquals(source.RootVisual, this))
                source.RootVisual = this;
        }
        catch (ArgumentException)
        {
            ClearStaleRootState();
            source.RootVisual = this;
        }
    }

    private bool IsRootAttached() =>
        SourceForThis() is HwndSource source && !source.IsDisposed && ReferenceEquals(source.RootVisual, this);

    private HwndSource? SourceForThis()
    {
        if (PresentationSource.FromVisual(this) is HwndSource direct)
            return direct;

        foreach (PresentationSource presentationSource in PresentationSource.CurrentSources)
        {
            if (presentationSource is HwndSource hwnd && ReferenceEquals(hwnd.RootVisual, this))
                return hwnd;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && HwndSource.FromHwnd(handle) is HwndSource fromHandle && !fromHandle.IsDisposed)
            return fromHandle;

        return null;
    }

    private bool VisualHasParent() => VisualParentField?.GetValue(this) != null;

    private bool IsMarkedRootElement() => IsRootElementProperty?.GetValue(this) is true;

    private void ClearStaleRootState()
    {
        var field = VisualParentField;
        if (field == null)
            AppLog.Write("root visual detach: parent field missing");
        else if (field.GetValue(this) != null)
            field.SetValue(this, null);

        if (IsRootElementProperty == null)
            AppLog.Write("root visual detach: root flag missing");
        else if (IsRootElementProperty.GetValue(this) is true)
            IsRootElementProperty.SetValue(this, false);
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
        if (BlockStartupClick(StartupSwitch, _startWithWindows))
            return;

        var wantOn = StartupSwitch.IsChecked == true;
        CommitStartup(
            wantOn ? StartupMode.StartWithWindows : StartupMode.Off,
            wantOn ? "Couldn't start with Windows." : "Couldn't turn off start with Windows.");
    }

    private void LaunchAtLoginSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (BlockStartupClick(LaunchAtLoginSwitch, _launchAtLogin))
            return;

        var wantOn = LaunchAtLoginSwitch.IsChecked == true;
        CommitStartup(
            wantOn ? StartupMode.LaunchAtLogin : StartupMode.Off,
            wantOn ? "Couldn't launch at login." : "Couldn't turn off launch at login.");
    }

    private bool BlockStartupClick(ToggleButton toggle, bool current)
    {
        if (!_writingSwitch && !_preview)
            return false;

        if (_preview)
            toggle.IsChecked = current;
        return true;
    }

    private void CommitStartup(StartupMode desired, string error)
    {
        var wrote = desired switch
        {
            StartupMode.StartWithWindows => StartupRegistration.Enable(awake: true),
            StartupMode.LaunchAtLogin => StartupRegistration.Enable(awake: false),
            _ => StartupRegistration.Disable()
        };

        if (!StartupRegistration.TryGetMode(out var actual))
        {
            _startupError = error;
            Sync();
            return;
        }

        ApplyStartupMode(actual);
        _startupError = wrote && actual == desired ? null : error;
        Sync();
        SaveSettings();
    }

    private void ReconcileStartup()
    {
        if (!StartupRegistration.TryGetMode(out var mode))
        {
            _startupError = "Couldn't read start with Windows.";
            return;
        }

        var start = mode == StartupMode.StartWithWindows;
        var launch = mode == StartupMode.LaunchAtLogin;
        if (start != _startWithWindows || launch != _launchAtLogin)
            AppLog.Write("startup preference updated from registry");

        ApplyStartupMode(mode);
    }

    private void ApplyStartupMode(StartupMode mode)
    {
        _startWithWindows = mode == StartupMode.StartWithWindows;
        _launchAtLogin = mode == StartupMode.LaunchAtLogin;
    }

    private void ArmKeepAwakeAtStartup()
    {
        try
        {
            _awake.Start(SelectedDuration());
            if (_awake.IsRunning && string.IsNullOrEmpty(_awake.LastError))
                AppLog.Write("startup keep-awake armed");
            else
                AppLog.Write("startup keep-awake failed: " + (_awake.LastError ?? "not running"));
        }
        catch (Exception ex)
        {
            AppLog.Write("startup keep-awake failed: " + ex.Message);
        }
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
        LaunchAtLoginSwitch.IsChecked = _launchAtLogin;
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
        TimePanel.Visibility = Visibility.Visible;
        SpinnerGrid.Visibility = Visibility.Visible;
        SpinnerGrid.IsEnabled = true;
        SpinnerGrid.Opacity = 1;
        HourBox.IsEnabled = true;
        MinuteBox.IsEnabled = true;
        SecondBox.IsEnabled = true;
        HourBox.Opacity = 1;
        MinuteBox.Opacity = 1;
        SecondBox.Opacity = 1;

        TimeSpan? remaining = null;
        if (_previewOn)
            remaining = _previewRemaining;
        else if (_awake.IsRunning && _timerEnabled)
            remaining = _awake.Remaining;

        var counting = _timerEnabled && remaining is not null;
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

        var committed = (_hours, _minutes, _seconds);
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
        CommitDuration((_hours, _minutes, _seconds) != committed);
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

        var committed = (_hours, _minutes, _seconds);
        ReadBoxes();
        WriteBoxes();
        CommitDuration((_hours, _minutes, _seconds) != committed);
    }

    private void CommitDuration(bool changed)
    {
        SaveSettings();
        if (changed && _awake.IsRunning && _timerEnabled)
            _awake.SetTurnOffAfter(SelectedDuration());
        else
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
            SettingsStore.Save(new AppSettings(_hours, _minutes, _seconds, _timerEnabled, _startWithWindows, _launchAtLogin));
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
