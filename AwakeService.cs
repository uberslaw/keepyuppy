using System.Windows.Threading;

namespace KeepyUppy;

internal sealed class AwakeService : IDisposable
{
    private static readonly TimeSpan IdleBeforeNudge = TimeSpan.FromSeconds(45);

    private readonly DispatcherTimer _timer;
    private int _nudgeFailures;
    private bool _disposed;

    public AwakeService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public bool IsRunning { get; private set; }

    public DateTime? TurnOffAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public event Action? StateChanged;

    public event Action? Clock;

    public event Action? Elapsed;

    public TimeSpan? Remaining
    {
        get
        {
            if (TurnOffAtUtc is not DateTime end)
                return null;

            var left = end - DateTime.UtcNow;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    public void Start(TimeSpan turnOffAfter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastError = null;
        _nudgeFailures = 0;
        IsRunning = true;
        TurnOffAtUtc = turnOffAfter > TimeSpan.Zero ? DateTime.UtcNow.Add(turnOffAfter) : null;
        NativeMethods.SetAwakeExecutionState();
        if (!NativeMethods.NudgeMouse())
            LastError = "Windows did not accept the mouse nudge.";

        _timer.Start();
        AppLog.Write("awake on, turnOffAfter=" + turnOffAfter);
        StateChanged?.Invoke();
    }

    public void SetTurnOffAfter(TimeSpan turnOffAfter)
    {
        if (_disposed || !IsRunning)
            return;

        TurnOffAtUtc = turnOffAfter > TimeSpan.Zero ? DateTime.UtcNow.Add(turnOffAfter) : null;
        AppLog.Write("turnOffAfter=" + turnOffAfter);
        StateChanged?.Invoke();
    }

    public void Stop(string reason)
    {
        if (_disposed)
            return;

        var wasRunning = IsRunning;
        _timer.Stop();
        IsRunning = false;
        TurnOffAtUtc = null;
        NativeMethods.ClearExecutionState();
        if (wasRunning)
            AppLog.Write("awake off: " + reason);

        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        IsRunning = false;
        TurnOffAtUtc = null;
        NativeMethods.ClearExecutionState();
    }

    private void Tick()
    {
        if (!IsRunning)
            return;

        if (TurnOffAtUtc is DateTime end && DateTime.UtcNow >= end)
        {
            Stop("timer");
            Elapsed?.Invoke();
            return;
        }

        NativeMethods.SetAwakeExecutionState();
        if (NativeMethods.GetIdleTime() >= IdleBeforeNudge)
        {
            var errorBefore = LastError;
            if (!NativeMethods.NudgeMouse())
            {
                _nudgeFailures++;
                if (_nudgeFailures >= 3)
                    LastError = "Windows did not accept the mouse nudge.";
            }
            else
            {
                _nudgeFailures = 0;
                LastError = null;
            }

            if (LastError != errorBefore)
                StateChanged?.Invoke();
        }

        Clock?.Invoke();
    }
}
