namespace NexusClick.Engine;

public enum EngineState { Idle, Running, Paused, Stopping, Faulted }
public enum InputKind { Key, MouseLeft, MouseRight, MouseMiddle }
public readonly record struct ScreenPoint(int X, int Y);
public sealed record InputPlan(InputKind Kind, int VirtualKey, double FrequencyHz, int HoldMilliseconds, long? RepeatCount = null, ScreenPoint? FixedPosition = null, bool? ExtendedKey = null, int? MaximumDurationSeconds = null, int RandomIntervalPercent = 0, int BurstCount = 0, int BurstRestMilliseconds = 1000)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (Kind == InputKind.Key && (VirtualKey < 1 || VirtualKey > 254)) throw new ArgumentOutOfRangeException(nameof(VirtualKey));
        if (Kind == InputKind.Key && FixedPosition is not null) throw new ArgumentException("Fixed position is only valid for mouse input.");
        if (Kind != InputKind.Key && ExtendedKey is not null) throw new ArgumentException("Extended-key setting is only valid for keyboard input.");
        if (!double.IsFinite(FrequencyHz) || FrequencyHz < 0.1 || FrequencyHz > 50) throw new ArgumentOutOfRangeException(nameof(FrequencyHz));
        if (HoldMilliseconds < 1 || HoldMilliseconds > 5000) throw new ArgumentOutOfRangeException(nameof(HoldMilliseconds));
        if (RepeatCount is <= 0 or > 10_000_000) throw new ArgumentOutOfRangeException(nameof(RepeatCount));
        if (MaximumDurationSeconds is <= 0 or > 86_400) throw new ArgumentOutOfRangeException(nameof(MaximumDurationSeconds));
        if (RandomIntervalPercent is < 0 or > 50) throw new ArgumentOutOfRangeException(nameof(RandomIntervalPercent));
        if (BurstCount is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(BurstCount));
        if (BurstRestMilliseconds is < 1 or > 60000) throw new ArgumentOutOfRangeException(nameof(BurstRestMilliseconds));
    }
    public TimeSpan IntervalForSample(double sample)
    {
        if (!double.IsFinite(sample) || sample < 0 || sample > 1) throw new ArgumentOutOfRangeException(nameof(sample));
        var nominalMilliseconds = 1000.0 / FrequencyHz;
        var multiplier = 1 + (2 * sample - 1) * RandomIntervalPercent / 100.0;
        return TimeSpan.FromMilliseconds(Math.Max(20, nominalMilliseconds * multiplier));
    }
}
public sealed record EngineSnapshot(EngineState State, long Completed, string? Error, TimeSpan ActiveTime, bool TimedOut = false, double RecentFrequencyHz = 0)
{
    public double AverageFrequencyHz => ActiveTime.TotalSeconds > 0 ? Completed / ActiveTime.TotalSeconds : 0;
}
public interface IInputSink { void Down(InputPlan plan); void Up(InputPlan plan); }

/// <summary>A single-task engine. StopAsync waits for cleanup; inspect HasPendingRelease for failed releases.</summary>
public sealed class InputEngine : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly ReleaseGuardInputSink _sink;
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private TaskCompletionSource<bool>? _resume;
    private EngineState _state;
    private long _completed;
    private string? _error;
    private bool _disposed;
    private long _activeTicks;
    private long _activeSince;
    private bool _clockRunning;
    private bool _stopRequested, _timedOut;
    private readonly Queue<long> _recentCompletions = new();
    public InputEngine(IInputSink sink) => _sink = sink as ReleaseGuardInputSink ?? new ReleaseGuardInputSink(sink);
    public bool HasPendingRelease => _sink.HasPendingRelease;
    public void RetryPendingRelease()
    {
        lock (_sync)
        {
            if (_worker is { IsCompleted: false }) throw new InvalidOperationException("Wait for the task to finish before retrying release.");
            _sink.RetryPendingRelease();
        }
    }
    public EngineSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var ticks = _activeTicks + (_clockRunning ? System.Diagnostics.Stopwatch.GetTimestamp() - _activeSince : 0);
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                TrimRecent(now);
                var recent = _state == EngineState.Running ? _recentCompletions.Count / 2.0 : 0;
                return new(_state, Interlocked.Read(ref _completed), _error, TimeSpan.FromSeconds((double)ticks / System.Diagnostics.Stopwatch.Frequency), _timedOut, recent);
            }
        }
    }
    private void TrimRecent(long now)
    {
        while (_recentCompletions.Count > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_recentCompletions.Peek(), now).TotalSeconds >= 2)
            _recentCompletions.Dequeue();
    }
    private void ClockStart() { _activeSince = System.Diagnostics.Stopwatch.GetTimestamp(); _clockRunning = true; }
    private void ClockStop()
    {
        if (!_clockRunning) return;
        _activeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _activeSince;
        _clockRunning = false;
    }

    public void Start(InputPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan); plan.Validate();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is { IsCompleted: false }) throw new InvalidOperationException("An input task is already active.");
            if (_sink.HasPendingRelease) throw new InputAlreadyPendingException();
            _cancel?.Dispose(); _cancel = new CancellationTokenSource();
            _completed = 0; _error = null; _state = EngineState.Running; _resume = null; _activeTicks = 0; _stopRequested = false; _timedOut = false; _recentCompletions.Clear(); ClockStart();
            if (plan.MaximumDurationSeconds is int seconds) _cancel.CancelAfter(TimeSpan.FromSeconds(seconds));
            _worker = Task.Run(() => RunAsync(plan, _cancel.Token));
        }
    }
    public bool Pause()
    {
        lock (_sync)
        {
            if (_state != EngineState.Running) return false;
            _resume = new(TaskCreationOptions.RunContinuationsAsynchronously); ClockStop(); _recentCompletions.Clear(); _state = EngineState.Paused; return true;
        }
    }
    public bool Resume()
    {
        lock (_sync)
        {
            if (_state != EngineState.Paused) return false;
            _state = EngineState.Running; ClockStart(); _resume?.TrySetResult(true); _resume = null; return true;
        }
    }
    public async Task StopAsync()
    {
        Task? worker;
        lock (_sync)
        {
            worker = _worker;
            if (worker is { IsCompleted: false } && _state is EngineState.Running or EngineState.Paused or EngineState.Stopping)
            {
                _state = EngineState.Stopping;
                _stopRequested = true;
                ClockStop();
                _cancel!.Cancel();
                _resume?.TrySetCanceled();
            }
        }
        if (worker is not null) await worker.ConfigureAwait(false);
    }
    private async Task RunAsync(InputPlan plan, CancellationToken token)
    {
        try
        {
            while (!plan.RepeatCount.HasValue || Interlocked.Read(ref _completed) < plan.RepeatCount.Value)
            {
                Task? gate; lock (_sync) gate = _state == EngineState.Paused ? _resume?.Task : null;
                if (gate is not null) await gate.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                long started = 0;
                bool attempted = false;
                try
                {
                    // Serialize the decision to press with Pause and Stop. An already
                    // pressed input is released at the end of its current hold.
                    lock (_sync)
                    {
                        if (_state == EngineState.Paused) continue;
                        token.ThrowIfCancellationRequested();
                        attempted = true;
                        started = System.Diagnostics.Stopwatch.GetTimestamp();
                        try { _sink.Down(plan); }
                        catch (InputAlreadyPendingException) { attempted = false; throw; }
                    }
                    await Task.Delay(plan.HoldMilliseconds, token).ConfigureAwait(false);
                }
                finally { if (attempted) _sink.Up(plan); }
                var completed = Interlocked.Increment(ref _completed);
                lock (_sync) { var now = System.Diagnostics.Stopwatch.GetTimestamp(); TrimRecent(now); if (_state == EngineState.Running) _recentCompletions.Enqueue(now); }
                if (plan.RepeatCount.HasValue && completed >= plan.RepeatCount.Value) break;
                var interval = plan.IntervalForSample(plan.RandomIntervalPercent == 0 ? 0.5 : Random.Shared.NextDouble());
                var remaining = interval - System.Diagnostics.Stopwatch.GetElapsedTime(started);
                if (plan.BurstCount > 0 && completed % plan.BurstCount == 0)
                    remaining = TimeSpan.FromMilliseconds(Math.Max(remaining.TotalMilliseconds, plan.BurstRestMilliseconds));
                if (remaining > TimeSpan.Zero) await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)), token).ConfigureAwait(false);
            }
            lock (_sync) { ClockStop(); _state = EngineState.Idle; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { lock (_sync) { ClockStop(); _timedOut = !_stopRequested && plan.MaximumDurationSeconds.HasValue; _state = EngineState.Idle; } }
        catch (Exception ex) { lock (_sync) { ClockStop(); _error = ex.Message; _state = EngineState.Faulted; } }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        await StopAsync().ConfigureAwait(false);
        try { _sink.RetryPendingRelease(); }
        finally { lock (_sync) { _cancel?.Dispose(); _cancel = null; } }
    }
}
