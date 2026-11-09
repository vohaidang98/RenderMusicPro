namespace RenderMusicPro.Services;

public sealed class RenderTimeEstimator
{
    private readonly object _gate = new();
    private int _total, _completed;
    private double _itemStart, _completedSeconds, _stageStart, _stageTarget;
    private long _stageMicroseconds;
    private bool _stageActive, _stageIsFinal, _finished;

    public void Reset()
    {
        lock (_gate)
        {
            _total = _completed = 0;
            _itemStart = _completedSeconds = _stageStart = _stageTarget = 0;
            _stageMicroseconds = 0;
            _stageActive = _stageIsFinal = _finished = false;
        }
    }
    public void SetTotal(int total) { lock (_gate) _total = Math.Max(0, total); }
    public void BeginItem(double elapsed) { lock (_gate) _itemStart = elapsed; }
    public void CompleteItem(double elapsed)
    {
        lock (_gate) { _completedSeconds += Math.Max(0, elapsed - _itemStart); _completed++; _stageActive = false; }
    }
    public void Finish() { lock (_gate) { _finished = true; _stageActive = false; } }
    public bool Finished { get { lock (_gate) return _finished; } }
    public bool Finalizing { get { lock (_gate) return _stageActive && _stageIsFinal &&
        _stageTarget > 0 && _stageMicroseconds / 1_000_000d >= _stageTarget * 0.995; } }
    public void BeginStage(double elapsed, double target, bool final)
    {
        lock (_gate) { _stageStart = elapsed; _stageTarget = target; _stageMicroseconds = 0; _stageActive = true; _stageIsFinal = final; }
    }
    public void ReportMicroseconds(long value)
    {
        lock (_gate) { if (_stageActive && value > _stageMicroseconds) _stageMicroseconds = value; }
    }
    public void EndStage() { lock (_gate) _stageActive = false; }

    public (TimeSpan? Remaining, bool Initial, TimeSpan? StageRemaining) Estimate(double elapsed)
    {
        lock (_gate)
        {
            if (_finished) return (TimeSpan.Zero, false, null);
            double stageElapsed = Math.Max(0, elapsed - _stageStart);
            double processed = _stageMicroseconds / 1_000_000d;
            double fraction = _stageTarget > 0 ? processed / _stageTarget : 0;
            double? stageRemaining = _stageActive && stageElapsed >= 2 && fraction > 0.001 && fraction < 0.995
                ? stageElapsed * (1 - fraction) / fraction : null;
            double? remaining = null;
            int pending = Math.Max(0, _total - _completed);
            if (stageRemaining.HasValue && _stageIsFinal && pending > 0)
            {
                double observedItem = Math.Max(0, _stageStart - _itemStart) + stageElapsed + stageRemaining.Value;
                double typicalItem = _completed > 0 ? _completedSeconds / _completed : observedItem;
                remaining = stageRemaining.Value + typicalItem * (pending - 1);
            }
            else if (_completed > 0 && pending > 0)
            {
                double estimate = _completedSeconds / _completed * pending - Math.Max(0, elapsed - _itemStart);
                if (estimate > 0) remaining = estimate;
            }
            return (SafeSpan(remaining), _completed == 0, SafeSpan(stageRemaining));
        }
    }

    private static TimeSpan? SafeSpan(double? seconds) => seconds.HasValue && double.IsFinite(seconds.Value) &&
        seconds.Value >= 0 && seconds.Value < 365 * 24 * 3600
        ? TimeSpan.FromSeconds(seconds.Value) : null;
}
