using Forms = System.Windows.Forms;

namespace KeepyUppy;

internal sealed class TrayController : IDisposable
{
    private readonly MainWindow _window;
    private readonly AwakeService _awake;
    private readonly Forms.NotifyIcon _notify;
    private readonly Forms.ToolStripMenuItem _toggle;
    private readonly System.Drawing.Icon _onIcon;
    private readonly System.Drawing.Icon _offIcon;
    private bool _shownOn;
    private bool _disposed;

    public TrayController(MainWindow window, AwakeService awake)
    {
        _window = window;
        _awake = awake;
        _onIcon = BalloonIcons.Create(awake: true);
        _offIcon = BalloonIcons.Create(awake: false);

        _toggle = new Forms.ToolStripMenuItem("Keep awake", null, (_, _) => _window.ToggleFromUser());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => _window.BringUp());
        menu.Items.Add(_toggle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _window.RequestExit());

        _notify = new Forms.NotifyIcon
        {
            Icon = _offIcon,
            Visible = true,
            Text = "KeepyUppy - off",
            ContextMenuStrip = menu
        };
        _notify.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                _window.BringUp();
        };
    }

    public void Sync(bool on)
    {
        if (_disposed)
            return;

        if (_shownOn != on)
        {
            _notify.Icon = on ? _onIcon : _offIcon;
            _shownOn = on;
        }

        _toggle.Checked = on;
        RefreshTooltip();
    }

    public void RefreshTooltip()
    {
        if (_disposed)
            return;

        var text = !_awake.IsRunning
            ? "KeepyUppy - off"
            : _awake.Remaining is TimeSpan left
                ? "KeepyUppy - on " + TimeText.Hms(left)
                : "KeepyUppy - on";

        _notify.Text = text.Length <= 63 ? text : text[..63];
    }

    public void ShowTip(string title, string message)
    {
        if (_disposed)
            return;

        _notify.BalloonTipTitle = title;
        _notify.BalloonTipText = message;
        _notify.ShowBalloonTip(3500);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notify.Visible = false;
        _notify.Dispose();
        _onIcon.Dispose();
        _offIcon.Dispose();
    }
}
