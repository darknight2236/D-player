using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DPlayer.Tests.TestSupport;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state,
        TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            timer.Change(dueTime, period);
            _timers.Add(timer);
        }
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        DateTimeOffset target;
        lock (_gate) target = _now + delta;
        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => !t.Disposed && t.Due <= target)
                    .OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }
                _now = next.Due;
                next.Due = next.Period > TimeSpan.Zero
                    ? _now + next.Period : DateTimeOffset.MaxValue;
            }
            next.Callback(next.State);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        internal readonly TimerCallback Callback;
        internal readonly object? State;
        internal DateTimeOffset Due = DateTimeOffset.MaxValue;
        internal TimeSpan Period;
        internal bool Disposed;

        internal ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            Callback = callback;
            State = state;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(period));
            lock (_owner._gate)
            {
                if (Disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue : _owner._now + dueTime;
                Period = period;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                Disposed = true;
                _owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
