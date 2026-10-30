namespace NexusClick.Engine;

/// <summary>Owns one input task at a time across click and macro engines.</summary>
public sealed class TaskCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly InputEngine _click;
    private readonly MacroRunner _macro;
    private readonly ReleaseGuardInputSink _sink;
    private bool _stopping, _disposed;
    private Task? _stopTask;
    public TaskCoordinator(IInputSink sink) { _sink = new(sink); _click = new(_sink); _macro = new(_sink); }
    public bool HasPendingRelease => _sink.HasPendingRelease;
    public void RetryPendingRelease()
    {
        lock (_sync)
        {
            if (_stopping || Active(_click.Snapshot.State) || Active(_macro.Snapshot.State)) throw new InvalidOperationException("Stop the active task first.");
            _click.RetryPendingRelease();
            _macro.RetryPendingRelease();
        }
    }
    public EngineSnapshot ClickSnapshot => _click.Snapshot;
    public MacroSnapshot MacroSnapshot => _macro.Snapshot;
    private static bool Active(EngineState state) => state is EngineState.Running or EngineState.Paused or EngineState.Stopping;
    private void CheckStart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping || Active(_click.Snapshot.State) || Active(_macro.Snapshot.State)) throw new InvalidOperationException("Another task is active or stopping.");
        if (_sink.HasPendingRelease) throw new InputAlreadyPendingException();
    }
    public void StartClick(InputPlan plan) { lock (_sync) { CheckStart(); _click.Start(plan); } }
    public void StartMacro(MacroPlan plan) { lock (_sync) { CheckStart(); _macro.Start(plan); } }
    public bool Pause()
    {
        lock (_sync) { if (_disposed || _stopping) return false; return _click.Pause() || _macro.Pause(); }
    }
    public bool Resume()
    {
        lock (_sync) { if (_disposed || _stopping) return false; return _click.Resume() || _macro.Resume(); }
    }
    /// <summary>Stop both engines and retry unresolved releases. A release failure is propagated.</summary>
    public Task StopAsync()
    {
        lock (_sync)
        {
            if (_stopping) return _stopTask!;
            _stopping = true;
            return _stopTask = StopCoreAsync();
        }
    }
    private async Task StopCoreAsync()
    {
        try { await Task.WhenAll(_click.StopAsync(), _macro.StopAsync()).ConfigureAwait(false); _sink.RetryPendingRelease(); }
        finally { lock (_sync) _stopping = false; }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        try { await StopAsync().ConfigureAwait(false); }
        finally
        {
            try { await _click.DisposeAsync().ConfigureAwait(false); }
            finally { await _macro.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
