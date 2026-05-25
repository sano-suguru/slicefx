namespace Slice.Lambda.FunctionPerFeature.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime);
        _timers.Add(timer);
        return timer;
    }

    internal TimeSpan? DueTime => _timers.SingleOrDefault()?.DueTime;

    internal void FireAll()
    {
        foreach (var timer in _timers.ToArray())
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        private bool _disposed;

        internal TimeSpan DueTime { get; private set; } = dueTime;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            return !_disposed;
        }

        public void Dispose()
            => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }
    }
}
