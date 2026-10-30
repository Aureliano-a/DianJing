namespace NexusClick.Engine;

public enum MacroStepKind { Key, MouseLeft, MouseRight, MouseMiddle, Wait }
public sealed record MacroStep(MacroStepKind Kind, int VirtualKey = 0, int DurationMilliseconds = 30)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException("Unknown macro step.");
        if (Kind == MacroStepKind.Key && VirtualKey is < 1 or > 254) throw new InvalidDataException("Invalid macro key.");
        if (DurationMilliseconds < 1 || DurationMilliseconds > (Kind == MacroStepKind.Wait ? 60_000 : 5_000))
            throw new InvalidDataException("Invalid macro step duration.");
    }
}
public sealed record MacroPlan(IReadOnlyList<MacroStep> Steps, int RepeatCount, int MaximumWallSeconds)
{
    public void Validate()
    {
        if (Steps is null || Steps.Count is < 1 or > 1000) throw new InvalidDataException("A macro must contain 1–1000 steps.");
        if (RepeatCount is < 1 or > 10_000 || (long)Steps.Count * RepeatCount > 100_000) throw new InvalidDataException("Macro iteration limit exceeded.");
        if (MaximumWallSeconds is < 1 or > 86_400) throw new InvalidDataException("Invalid macro time limit.");
        foreach (var step in Steps) { if (step is null) throw new InvalidDataException("Null macro step."); step.Validate(); }
    }
}
public sealed record MacroSnapshot(EngineState State, int Cycle, int StepIndex, long CompletedSteps, string? Error);

/// <summary>Sequential, bounded macro playback. No recording or nested macro steps are supported.</summary>
public sealed class MacroRunner : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly ReleaseGuardInputSink _sink;
    private CancellationTokenSource? _stop;
    private CancellationTokenSource? _deadline;
    private CancellationTokenSource? _linked;
    private TaskCompletionSource<bool>? _resume;
    private Task? _worker;
    private EngineState _state;
    private int _cycle, _stepIndex = -1;
    private long _completed;
    private string? _error;
    private bool _disposed;
    public MacroRunner(IInputSink sink) => _sink = sink as ReleaseGuardInputSink ?? new ReleaseGuardInputSink(sink);
    public bool HasPendingRelease => _sink.HasPendingRelease;
    public void RetryPendingRelease()
    {
        lock (_sync)
        {
            if (_worker is { IsCompleted: false }) throw new InvalidOperationException("Wait for the macro to finish before retrying release.");
            _sink.RetryPendingRelease();
        }
    }
    public MacroSnapshot Snapshot { get { lock (_sync) return new(_state, _cycle, _stepIndex, _completed, _error); } }
    public void Start(MacroPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        // Copy the list first so caller edits cannot introduce unvalidated steps during execution.
        var snapshot = plan with { Steps = plan.Steps?.ToArray()! };
        snapshot.Validate();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is { IsCompleted: false }) throw new InvalidOperationException("A macro is already running.");
            if (_sink.HasPendingRelease) throw new InputAlreadyPendingException();
            _linked?.Dispose(); _deadline?.Dispose(); _stop?.Dispose();
            _stop = new CancellationTokenSource();
            _deadline = new CancellationTokenSource(TimeSpan.FromSeconds(snapshot.MaximumWallSeconds));
            _linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, _deadline.Token);
            _state = EngineState.Running; _cycle = 0; _stepIndex = -1; _completed = 0; _error = null; _resume = null;
            var token = _linked.Token;
            _worker = Task.Run(() => RunAsync(snapshot, token));
        }
    }
    public bool Pause()
    {
        lock (_sync)
        {
            if (_state != EngineState.Running) return false;
            _state = EngineState.Paused; _resume = new(TaskCreationOptions.RunContinuationsAsynchronously); return true;
        }
    }
    public bool Resume()
    {
        lock (_sync)
        {
            if (_state != EngineState.Paused) return false;
            _state = EngineState.Running; _resume?.TrySetResult(true); _resume = null; return true;
        }
    }
    public async Task StopAsync()
    {
        Task? task;
        lock (_sync)
        {
            task = _worker;
            if (task is { IsCompleted: false } && _state is EngineState.Running or EngineState.Paused or EngineState.Stopping)
            {
                _state = EngineState.Stopping; _stop!.Cancel(); _resume?.TrySetCanceled();
            }
        }
        if (task is not null) await task.ConfigureAwait(false);
    }
    private async Task WaitIfPausedAsync(CancellationToken token)
    {
        Task? gate; lock (_sync) gate = _state == EngineState.Paused ? _resume?.Task : null;
        if (gate is not null) await gate.WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }
    private async Task WaitStepAsync(int milliseconds, CancellationToken token)
    {
        // Bound pause latency during explicit waits without a busy loop.
        int remaining = milliseconds;
        while (remaining > 0)
        {
            await WaitIfPausedAsync(token).ConfigureAwait(false);
            var chunk = Math.Min(remaining, 50);
            await Task.Delay(chunk, token).ConfigureAwait(false);
            remaining -= chunk;
        }
    }
    private async Task RunAsync(MacroPlan plan, CancellationToken token)
    {
        try
        {
            for (int cycle = 0; cycle < plan.RepeatCount; cycle++)
                for (int index = 0; index < plan.Steps.Count; index++)
                {
                    await WaitIfPausedAsync(token).ConfigureAwait(false);
                    var step = plan.Steps[index];
                    lock (_sync) { _cycle = cycle + 1; _stepIndex = index; }
                    if (step.Kind == MacroStepKind.Wait) await WaitStepAsync(step.DurationMilliseconds, token).ConfigureAwait(false);
                    else
                    {
                        var kind = step.Kind switch { MacroStepKind.Key => InputKind.Key, MacroStepKind.MouseLeft => InputKind.MouseLeft, MacroStepKind.MouseRight => InputKind.MouseRight, MacroStepKind.MouseMiddle => InputKind.MouseMiddle, _ => throw new InvalidDataException("Invalid step.") };
                        var input = new InputPlan(kind, step.VirtualKey, 1, step.DurationMilliseconds, 1);
                        bool attempted = false;
                        try
                        {
                            while (true)
                            {
                                await WaitIfPausedAsync(token).ConfigureAwait(false);
                                lock (_sync)
                                {
                                    if (_state == EngineState.Paused) continue;
                                    token.ThrowIfCancellationRequested();
                                    attempted = true;
                                    try { _sink.Down(input); }
                                    catch (InputAlreadyPendingException) { attempted = false; throw; }
                                    break;
                                }
                            }
                            // An in-progress hold completes before a pause takes effect; stop cancels it immediately.
                            await Task.Delay(step.DurationMilliseconds, token).ConfigureAwait(false);
                        }
                        finally { if (attempted) _sink.Up(input); }
                    }
                    lock (_sync) _completed++;
                }
            lock (_sync) { _stepIndex = -1; _state = EngineState.Idle; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (_sync)
            {
                _stepIndex = -1;
                if (_deadline!.IsCancellationRequested && !_stop!.IsCancellationRequested) { _state = EngineState.Faulted; _error = "Macro exceeded the wall-clock limit."; }
                else _state = EngineState.Idle;
            }
        }
        catch (Exception ex) { lock (_sync) { _stepIndex = -1; _state = EngineState.Faulted; _error = ex.Message; } }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        await StopAsync().ConfigureAwait(false);
        try { _sink.RetryPendingRelease(); }
        finally { lock (_sync) { _linked?.Dispose(); _deadline?.Dispose(); _stop?.Dispose(); } }
    }
}
