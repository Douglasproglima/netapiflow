namespace NetApiFlow.Storage;

internal sealed class IntervalLogLimiter(TimeSpan interval, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (now < _nextAllowed)
            {
                return false;
            }

            _nextAllowed = now + interval;
            return true;
        }
    }
}
