namespace NexusClick.Engine;

/// <summary>Immutable step-list operations for a macro editor. An empty draft cannot be executed or saved as a MacroDocument.</summary>
public sealed class MacroDraft
{
    private readonly MacroStep[] _steps;
    public IReadOnlyList<MacroStep> Steps => Array.AsReadOnly(_steps);
    public MacroDraft(IEnumerable<MacroStep>? steps = null)
    {
        _steps = steps?.Take(1001).ToArray() ?? [];
        if (_steps.Length > 1000) throw new InvalidDataException("A macro draft cannot exceed 1000 steps.");
        foreach (var step in _steps) { if (step is null) throw new InvalidDataException("Null macro step."); step.Validate(); }
    }
    public MacroDraft Insert(int index, MacroStep step)
    {
        ArgumentNullException.ThrowIfNull(step); step.Validate();
        if ((uint)index > (uint)_steps.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (_steps.Length == 1000) throw new InvalidDataException("A macro draft cannot exceed 1000 steps.");
        var list = _steps.ToList(); list.Insert(index, step); return new MacroDraft(list);
    }
    public MacroDraft Replace(int index, MacroStep step)
    {
        ArgumentNullException.ThrowIfNull(step); step.Validate(); CheckIndex(index);
        var copy = (MacroStep[])_steps.Clone(); copy[index] = step; return new MacroDraft(copy);
    }
    public MacroDraft Remove(int index)
    {
        CheckIndex(index); var list = _steps.ToList(); list.RemoveAt(index); return new MacroDraft(list);
    }
    /// <summary>Move an existing step to its final zero-based index.</summary>
    public MacroDraft Move(int from, int to)
    {
        CheckIndex(from); CheckIndex(to);
        var list = _steps.ToList(); var step = list[from]; list.RemoveAt(from); list.Insert(to, step); return new MacroDraft(list);
    }
    public MacroDraft Duplicate(int index)
    {
        CheckIndex(index); return Insert(index + 1, _steps[index] with { });
    }
    public MacroPlan ToPlan(int repeatCount, int maximumWallSeconds)
    {
        var plan = new MacroPlan(Array.AsReadOnly((MacroStep[])_steps.Clone()), repeatCount, maximumWallSeconds);
        plan.Validate(); return plan;
    }
    private void CheckIndex(int index) { if ((uint)index >= (uint)_steps.Length) throw new ArgumentOutOfRangeException(nameof(index)); }
}
