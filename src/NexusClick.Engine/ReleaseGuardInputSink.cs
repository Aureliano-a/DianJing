namespace NexusClick.Engine;

public sealed class InputAlreadyPendingException : InvalidOperationException
{
    public InputAlreadyPendingException() : base("A previous input release remains unresolved.") { }
}
/// <summary>Tracks an input until its release was accepted by the underlying sender.</summary>
internal sealed class ReleaseGuardInputSink : IInputSink
{
    private readonly object _sync = new();
    private readonly IInputSink _inner;
    private InputPlan? _pending;
    public ReleaseGuardInputSink(IInputSink inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    public bool HasPendingRelease { get { lock (_sync) return _pending is not null; } }
    public void Down(InputPlan plan)
    {
        lock (_sync)
        {
            if (_pending is not null) throw new InputAlreadyPendingException();
            _pending = plan;
            _inner.Down(plan);
        }
    }
    public void Up(InputPlan plan)
    {
        lock (_sync)
        {
            if (_pending is null || _pending.Kind != plan.Kind || _pending.VirtualKey != plan.VirtualKey)
                throw new InvalidOperationException("Release does not match the pending input.");
            _inner.Up(plan);
            _pending = null;
        }
    }
    public void RetryPendingRelease()
    {
        lock (_sync)
        {
            if (_pending is null) return;
            _inner.Up(_pending);
            _pending = null;
        }
    }
}
