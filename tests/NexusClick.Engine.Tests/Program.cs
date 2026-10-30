using NexusClick.Engine;

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task WaitUntil(Func<bool> predicate, int timeoutMs = 2000)
{
    var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!predicate()) { if (DateTime.UtcNow >= until) throw new TimeoutException(); await Task.Delay(5); }
}
var plan = new InputPlan(InputKind.Key, 65, 30, 5, 3);
var jitterPlan = plan with { FrequencyHz = 10, RandomIntervalPercent = 25 };
jitterPlan.Validate();
Assert(jitterPlan.IntervalForSample(0).TotalMilliseconds == 75 && jitterPlan.IntervalForSample(0.5).TotalMilliseconds == 100 && jitterPlan.IntervalForSample(1).TotalMilliseconds == 125, "random interval range incorrect");
Assert((plan with { FrequencyHz = 50, RandomIntervalPercent = 50 }).IntervalForSample(0).TotalMilliseconds == 20, "random interval exceeded the rate limit");
try { (plan with { RandomIntervalPercent = 51 }).Validate(); throw new Exception("random interval bound not enforced"); }
catch (ArgumentOutOfRangeException) { }
var timingSink = new TimingSink();
await using (var timingEngine = new InputEngine(timingSink))
{
    timingEngine.Start(plan with { FrequencyHz = 50, HoldMilliseconds = 1, RepeatCount = 5, RandomIntervalPercent = 50 });
    await WaitUntil(() => timingEngine.Snapshot.State == EngineState.Idle);
    var timestamps = timingSink.Timestamps;
    Assert(timestamps.Length == 5, "random timing task did not complete");
    for (var i = 1; i < timestamps.Length; i++)
        Assert(System.Diagnostics.Stopwatch.GetElapsedTime(timestamps[i - 1], timestamps[i]).TotalMilliseconds >= 19, "random timing exceeded the rate limit");
}
var burstSink = new TimingSink();
await using (var burstEngine = new InputEngine(burstSink))
{
    burstEngine.Start(plan with { FrequencyHz = 50, HoldMilliseconds = 1, RepeatCount = 4, BurstCount = 2, BurstRestMilliseconds = 250 });
    await WaitUntil(() => burstEngine.Snapshot.State == EngineState.Idle);
    var times = burstSink.Timestamps;
    Assert(times.Length == 4 && System.Diagnostics.Stopwatch.GetElapsedTime(times[1], times[2]).TotalMilliseconds >= 240, "burst rest or repeat count failed");
}
var restingSink = new FakeSink();
await using (var restingEngine = new InputEngine(restingSink))
{
    restingEngine.Start(plan with { FrequencyHz = 50, HoldMilliseconds = 1, RepeatCount = null, BurstCount = 1, BurstRestMilliseconds = 5000 });
    await WaitUntil(() => restingSink.Ups == 1);
    await restingEngine.StopAsync();
    Assert(restingEngine.Snapshot.State == EngineState.Idle && restingSink.Downs == 1 && restingSink.Ups == 1, "stop during burst rest failed");
}
try { (plan with { BurstCount = 1001 }).Validate(); throw new Exception("burst bound not enforced"); }
catch (ArgumentOutOfRangeException) { }
var sink = new FakeSink();
await using (var engine = new InputEngine(sink))
{
    engine.Start(plan);
    await WaitUntil(() => engine.Snapshot.State == EngineState.Idle);
    Assert(engine.Snapshot.Completed == 3 && sink.Downs == 3 && sink.Ups == 3, "repeat count or pairing failed");
}
var held = new FakeSink();
await using (var engine = new InputEngine(held))
{
    engine.Start(plan with { HoldMilliseconds = 2000, RepeatCount = null });
    await WaitUntil(() => held.Downs == 1);
    await engine.StopAsync();
    Assert(held.Ups == 1 && engine.Snapshot.State == EngineState.Idle, "stop must release input");
}
var timedHold = new FakeSink();
await using (var engine = new InputEngine(timedHold))
{
    engine.Start(plan with { HoldMilliseconds = 5000, RepeatCount = null, MaximumDurationSeconds = 1 });
    await WaitUntil(() => timedHold.Downs == 1);
    await WaitUntil(() => engine.Snapshot.State == EngineState.Idle, 4000);
    Assert(timedHold.Ups == 1 && engine.Snapshot.TimedOut && !engine.HasPendingRelease, "deadline did not release a held input");
}
var timedPause = new FakeSink();
await using (var engine = new InputEngine(timedPause))
{
    engine.Start(plan with { FrequencyHz = 1, RepeatCount = null, MaximumDurationSeconds = 1 });
    await WaitUntil(() => timedPause.Ups == 1);
    Assert(engine.Pause(), "deadline test could not pause");
    await WaitUntil(() => engine.Snapshot.State == EngineState.Idle, 4000);
    Assert(engine.Snapshot.TimedOut && timedPause.Downs == 1 && timedPause.Ups == 1, "deadline did not stop while paused");
}
try { (plan with { MaximumDurationSeconds = 86401 }).Validate(); throw new Exception("duration bound not enforced"); }
catch (ArgumentOutOfRangeException) { }
var failure = new FakeSink { FailDown = true };
await using (var engine = new InputEngine(failure))
{
    engine.Start(plan);
    await WaitUntil(() => engine.Snapshot.State == EngineState.Faulted);
    Assert(failure.Ups == 1 && engine.Snapshot.Error is not null, "fault must attempt release");
    await engine.StopAsync();
    Assert(engine.Snapshot.State == EngineState.Faulted, "stop should preserve a completed fault");
}
var releaseFailure = new FakeSink { FailUp = true };
await using (var engine = new InputEngine(releaseFailure))
{
    engine.Start(plan);
    await WaitUntil(() => engine.Snapshot.State == EngineState.Faulted);
    Assert(engine.Snapshot.Error is not null && engine.Snapshot.Completed == 0, "release failure must be visible");
    await engine.StopAsync();
    Assert(engine.HasPendingRelease, "failed release must remain pending");
    try { engine.Start(plan); throw new Exception("start accepted with unresolved release"); }
    catch (InputAlreadyPendingException) { }
    releaseFailure.FailUp = false;
    engine.RetryPendingRelease();
    Assert(!engine.HasPendingRelease, "successful retry should clear pending release");
    engine.Start(plan with { RepeatCount = 1 });
    await WaitUntil(() => engine.Snapshot.State == EngineState.Idle);
}
var paused = new FakeSink();
await using (var engine = new InputEngine(paused))
{
    engine.Start(plan with { FrequencyHz = 2, RepeatCount = null });
    await WaitUntil(() => paused.Ups == 1);
    Assert(engine.Snapshot.RecentFrequencyHz > 0, "recent frequency did not use completed operations");
    Assert(engine.Pause(), "pause failed");
    Assert(engine.Snapshot.RecentFrequencyHz == 0, "paused task reported an active frequency");
    var pausedTime = engine.Snapshot.ActiveTime;
    await Task.Delay(650);
    Assert(paused.Downs == 1 && engine.Snapshot.State == EngineState.Paused, "paused task continued");
    Assert(engine.Snapshot.ActiveTime == pausedTime, "paused time should not increase");
    Assert(engine.Resume(), "resume failed");
    await WaitUntil(() => paused.Downs >= 2);
    await engine.StopAsync();
    Assert(engine.Snapshot.ActiveTime > pausedTime && engine.Snapshot.AverageFrequencyHz > 0, "active time or frequency missing");
}
var disposalSink = new FakeSink();
var disposableEngine = new InputEngine(disposalSink);
disposableEngine.Start(plan with { HoldMilliseconds = 2000, RepeatCount = null });
await WaitUntil(() => disposalSink.Downs == 1);
try { disposableEngine.Start(plan); throw new Exception("duplicate start accepted"); }
catch (InvalidOperationException) { }
await disposableEngine.DisposeAsync();
Assert(disposalSink.Ups == 1, "dispose must release input");
await disposableEngine.DisposeAsync();
try { (plan with { FrequencyHz = 1000 }).Validate(); throw new Exception("frequency bound not enforced"); }
catch (ArgumentOutOfRangeException) { }
var desktop = new DesktopBounds(-1920, -200, 3840, 1280);
var firstPixel = DesktopCoordinates.Normalize(new ScreenPoint(-1920, -200), desktop);
var lastPixel = DesktopCoordinates.Normalize(new ScreenPoint(1919, 1079), desktop);
Assert(firstPixel.X >= 0 && firstPixel.Y >= 0 && lastPixel.X <= 65535 && lastPixel.Y <= 65535 && lastPixel.X > firstPixel.X && lastPixel.Y > firstPixel.Y, "virtual desktop mapping invalid");
try { DesktopCoordinates.Normalize(new ScreenPoint(-1921, 0), desktop); throw new Exception("outside point accepted"); }
catch (ArgumentOutOfRangeException) { }
try { (plan with { FixedPosition = new ScreenPoint(0, 0) }).Validate(); throw new Exception("keyboard position accepted"); }
catch (ArgumentException) { }
Assert(KeyboardInputFlags.ForVirtualKey(0x25, false) == 1 && KeyboardInputFlags.ForVirtualKey(0x25, true) == 3, "arrow key must include extended flag on down and up");
Assert(KeyboardInputFlags.ForVirtualKey(0x41, false) == 0 && KeyboardInputFlags.ForVirtualKey(0x41, true) == 2, "letter key flags incorrect");
Assert(KeyboardInputFlags.ForVirtualKey(0x0D, false, true) == 1 && KeyboardInputFlags.ForVirtualKey(0x0D, false) == 0, "keypad Enter override incorrect");
try { (new InputPlan(InputKind.MouseLeft, 0, 10, 30, ExtendedKey: true)).Validate(); throw new Exception("mouse extended flag accepted"); }
catch (ArgumentException) { }
var testDirectory = Path.Combine(Environment.CurrentDirectory, "work", "profile-tests", Guid.NewGuid().ToString("N"));
var profileFile = Path.Combine(testDirectory, "profiles.json");
try
{
    var store = new ProfileStore();
    var profile = new Profile(Guid.NewGuid(), "常用鼠标", plan, true);
    var document = new ProfileDocument(1, new[] { profile });
    await store.SaveAsync(profileFile, document);
    var loaded = await store.LoadAsync(profileFile);
    Assert(loaded.Profiles.Count == 1 && loaded.Profiles[0] == profile, "profile round trip failed");
    var updated = profile with { Name = "更新后的方案" };
    await store.SaveAsync(profileFile, new ProfileDocument(1, new[] { updated }));
    Assert((await store.LoadAsync(profileFile)).Profiles[0] == updated, "existing file was not replaced");
    var before = await File.ReadAllBytesAsync(profileFile);
    try { await store.SaveAsync(profileFile, new ProfileDocument(1, new[] { profile, profile })); throw new Exception("duplicate IDs accepted"); }
    catch (InvalidDataException) { }
    var afterInvalid = await File.ReadAllBytesAsync(profileFile);
    Assert(before.SequenceEqual(afterInvalid), "invalid save changed destination");
    try { ProfileStore.Parse(new byte[ProfileStore.MaximumBytes + 1]); throw new Exception("oversized input accepted"); }
    catch (InvalidDataException) { }
    try { ProfileStore.Parse("{broken"u8); throw new Exception("malformed JSON accepted"); }
    catch (InvalidDataException) { }
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { await store.SaveAsync(profileFile, document, cancel.Token); throw new Exception("cancelled save accepted"); }
    catch (OperationCanceledException) { }
    var afterCancel = await File.ReadAllBytesAsync(profileFile);
    Assert(before.SequenceEqual(afterCancel), "cancelled save changed destination");
    var macroStore = new MacroStore();
    var macroFile = Path.Combine(testDirectory, "macro.json");
    var macroDocument = new MacroDocument(1, Guid.NewGuid(), "测试宏", new MacroPlan(new[] { new MacroStep(MacroStepKind.Key, 65, 25), new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 50) }, 2, 10));
    await macroStore.SaveAsync(macroFile, macroDocument);
    var loadedMacro = await macroStore.LoadAsync(macroFile);
    Assert(loadedMacro.Id == macroDocument.Id && loadedMacro.Plan.Steps.SequenceEqual(macroDocument.Plan.Steps), "macro round trip failed");
    var originalMacro = await File.ReadAllBytesAsync(macroFile);
    try { await macroStore.SaveAsync(macroFile, macroDocument with { Plan = macroDocument.Plan with { RepeatCount = 100_001 } }); throw new Exception("invalid macro accepted"); }
    catch (InvalidDataException) { }
    var afterInvalidMacro = await File.ReadAllBytesAsync(macroFile);
    Assert(originalMacro.SequenceEqual(afterInvalidMacro), "invalid macro save changed destination");
}
finally
{
    if (Directory.Exists(testDirectory))
    {
        File.Delete(Path.Combine(testDirectory, "profiles.json"));
        File.Delete(Path.Combine(testDirectory, "macro.json"));
        Directory.Delete(testDirectory); // Only delete the empty directory created by this test.
    }
}
var macroSink = new SequenceSink();
await using (var macro = new MacroRunner(macroSink))
{
    var steps = new[] { new MacroStep(MacroStepKind.Key, 65, 5), new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 5), new MacroStep(MacroStepKind.MouseLeft, DurationMilliseconds: 5) };
    macro.Start(new MacroPlan(steps, 2, 5));
    await WaitUntil(() => macro.Snapshot.State == EngineState.Idle);
    Assert(macro.Snapshot.CompletedSteps == 6, "macro step count incorrect");
    Assert(string.Join(",", macroSink.Events) == "down:Key:65,up:Key:65,down:MouseLeft:0,up:MouseLeft:0,down:Key:65,up:Key:65,down:MouseLeft:0,up:MouseLeft:0", "macro order incorrect");
}
var macroHold = new SequenceSink();
await using (var macro = new MacroRunner(macroHold))
{
    macro.Start(new MacroPlan(new[] { new MacroStep(MacroStepKind.Key, 65, 2000) }, 1, 5));
    await WaitUntil(() => macroHold.Events.Count > 0);
    await macro.StopAsync();
    Assert(string.Join(",", macroHold.Events) == "down:Key:65,up:Key:65" && macro.Snapshot.State == EngineState.Idle, "stopped macro must release");
}
await using (var macro = new MacroRunner(new SequenceSink()))
{
    macro.Start(new MacroPlan(new[] { new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 400) }, 1, 5));
    await WaitUntil(() => macro.Snapshot.StepIndex == 0);
    Assert(macro.Pause(), "macro pause failed");
    await Task.Delay(500);
    Assert(macro.Snapshot.State == EngineState.Paused && macro.Snapshot.CompletedSteps == 0, "macro wait ran while paused");
    Assert(macro.Resume(), "macro resume failed");
    await WaitUntil(() => macro.Snapshot.State == EngineState.Idle);
}
var deadlineSink = new SequenceSink();
await using (var macro = new MacroRunner(deadlineSink))
{
    macro.Start(new MacroPlan(new[] { new MacroStep(MacroStepKind.Key, 65, 3000) }, 1, 1));
    await WaitUntil(() => macro.Snapshot.State == EngineState.Faulted, 3000);
    Assert(deadlineSink.Events.Count == 2 && macro.Snapshot.Error is not null, "deadline must release and report fault");
}
var macroFailure = new FakeSink { FailDown = true };
await using (var macro = new MacroRunner(macroFailure))
{
    macro.Start(new MacroPlan(new[] { new MacroStep(MacroStepKind.MouseRight) }, 1, 5));
    await WaitUntil(() => macro.Snapshot.State == EngineState.Faulted);
    Assert(macroFailure.Ups == 1 && macro.Snapshot.CompletedSteps == 0, "macro failure did not release or report correctly");
}
try { new MacroPlan(new[] { new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 1) }, 100_001, 60).Validate(); throw new Exception("macro bound not enforced"); }
catch (InvalidDataException) { }
var draft0 = new MacroDraft();
var draft1 = draft0.Insert(0, new MacroStep(MacroStepKind.Key, 65, 20)).Insert(1, new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 50));
var draft2 = draft1.Duplicate(0).Move(2, 0).Replace(1, new MacroStep(MacroStepKind.MouseLeft)).Remove(2);
Assert(draft0.Steps.Count == 0 && draft1.Steps.Count == 2, "draft edits modified an earlier snapshot");
Assert(draft2.Steps.Count == 2 && draft2.Steps[0].Kind == MacroStepKind.Wait && draft2.Steps[1].Kind == MacroStepKind.MouseLeft, "draft edit ordering incorrect");
Assert(draft2.ToPlan(2, 10).Steps.Count == 2, "valid draft did not create a plan");
try { draft0.ToPlan(1, 10); throw new Exception("empty draft accepted for execution"); }
catch (InvalidDataException) { }
try { draft1.Move(0, 2); throw new Exception("out-of-range move accepted"); }
catch (ArgumentOutOfRangeException) { }
var coordinatorSink = new SequenceSink();
await using (var coordinator = new TaskCoordinator(coordinatorSink))
{
    coordinator.StartClick(plan with { HoldMilliseconds = 1000, RepeatCount = null });
    await WaitUntil(() => coordinatorSink.Events.Count > 0);
    try { coordinator.StartMacro(new MacroPlan(new[] { new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 10) }, 1, 2)); throw new Exception("concurrent task accepted"); }
    catch (InvalidOperationException) { }
    await Task.WhenAll(coordinator.StopAsync(), coordinator.StopAsync());
    Assert(coordinatorSink.Events.Count == 2, "coordinator stop did not release input");
    coordinator.StartMacro(new MacroPlan(new[] { new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 10) }, 1, 2));
    await WaitUntil(() => coordinator.MacroSnapshot.State == EngineState.Idle);
    Assert(coordinator.MacroSnapshot.CompletedSteps == 1, "coordinator could not start next task");
}
var sharedFailure = new FakeSink { FailUp = true };
await using (var coordinator = new TaskCoordinator(sharedFailure))
{
    coordinator.StartClick(plan with { RepeatCount = 1 });
    await WaitUntil(() => coordinator.ClickSnapshot.State == EngineState.Faulted);
    Assert(coordinator.HasPendingRelease, "coordinator lost pending release");
    try { coordinator.StartMacro(new MacroPlan(new[] { new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 10) }, 1, 2)); throw new Exception("macro accepted unresolved click release"); }
    catch (InputAlreadyPendingException) { }
    try { await coordinator.StopAsync(); throw new Exception("failed retry not reported"); }
    catch (InvalidOperationException) { }
    Assert(coordinator.HasPendingRelease, "failed retry cleared pending release");
    sharedFailure.FailUp = false;
    coordinator.RetryPendingRelease();
    coordinator.StartMacro(new MacroPlan(new[] { new MacroStep(MacroStepKind.Wait, DurationMilliseconds: 10) }, 1, 2));
    await WaitUntil(() => coordinator.MacroSnapshot.State == EngineState.Idle);
}
var macroReleaseFailure = new FakeSink { FailUp = true };
await using (var coordinator = new TaskCoordinator(macroReleaseFailure))
{
    coordinator.StartMacro(new MacroPlan(new[] { new MacroStep(MacroStepKind.Key, 65, 10) }, 1, 2));
    await WaitUntil(() => coordinator.MacroSnapshot.State == EngineState.Faulted);
    try { coordinator.StartClick(plan); throw new Exception("click accepted unresolved macro release"); }
    catch (InputAlreadyPendingException) { }
    macroReleaseFailure.FailUp = false;
    await coordinator.StopAsync();
    Assert(!coordinator.HasPendingRelease, "stop did not recover pending macro release");
}
if (OperatingSystem.IsWindows())
{
    var mods = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;
    var first = new Hotkey(mods, 0x83); // F20; used only for registration tests.
    var emergency = new Hotkey(mods, 0x84);
    var rollback = new Hotkey(mods, 0x85);
    using var service = new WindowsHotkeyService(first, emergency, () => { }, () => { });
    Assert(service.IsRunning, "message loop did not start");
    using var other = new WindowsHotkeyService(new Hotkey(mods, 0x87), new Hotkey(mods, 0x88), () => { }, () => { });
    try { service.ChangeStartHotkey(new Hotkey(mods, 0x87)); throw new Exception("occupied replacement accepted"); }
    catch (System.ComponentModel.Win32Exception) { }
    try { using var verifyOld = new WindowsHotkeyService(first, new Hotkey(mods, 0x89), () => { }, () => { }); throw new Exception("old key lost after failed replacement"); }
    catch (System.ComponentModel.Win32Exception) { }
    service.ChangeStartHotkey(new Hotkey(mods, 0x8A));
    service.ChangeStartHotkey(new Hotkey(mods, 0x8C));
    using var verifyReleased = new WindowsHotkeyService(first, new Hotkey(mods, 0x8B), () => { }, () => { });
    using var verifyPriorReleased = new WindowsHotkeyService(new Hotkey(mods, 0x8A), new Hotkey(mods, 0x8D), () => { }, () => { });
    try
    {
        using var collision = new WindowsHotkeyService(first, rollback, () => { }, () => { });
        throw new Exception("hotkey conflict was not detected");
    }
    catch (System.ComponentModel.Win32Exception) { }
    using var released = new WindowsHotkeyService(new Hotkey(mods, 0x86), rollback, () => { }, () => { });
    Assert(released.IsRunning, "failed registration did not roll back emergency key");
}
Console.WriteLine("PASS: input and macro lifecycle, release recovery, task coordination, profiles, hotkey conflict and rollback");

sealed class FakeSink : IInputSink
{
    private int _downs, _ups;
    public int Downs => Volatile.Read(ref _downs);
    public int Ups => Volatile.Read(ref _ups);
    public bool FailDown { get; init; }
    public bool FailUp { get; set; }
    public void Down(InputPlan plan) { Interlocked.Increment(ref _downs); if (FailDown) throw new InvalidOperationException("simulated failure"); }
    public void Up(InputPlan plan) { Interlocked.Increment(ref _ups); if (FailUp) throw new InvalidOperationException("simulated release failure"); }
}
sealed class TimingSink : IInputSink
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<long> _timestamps = new();
    public long[] Timestamps => _timestamps.ToArray();
    public void Down(InputPlan plan) => _timestamps.Enqueue(System.Diagnostics.Stopwatch.GetTimestamp());
    public void Up(InputPlan plan) { }
}
sealed class SequenceSink : IInputSink
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _events = new();
    public IReadOnlyCollection<string> Events => _events.ToArray();
    public void Down(InputPlan plan) => _events.Enqueue($"down:{plan.Kind}:{plan.VirtualKey}");
    public void Up(InputPlan plan) => _events.Enqueue($"up:{plan.Kind}:{plan.VirtualKey}");
}
