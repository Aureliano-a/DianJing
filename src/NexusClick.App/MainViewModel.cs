using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using NexusClick.Engine;
namespace NexusClick.App;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly TaskCoordinator _engine;
    private readonly Func<int,bool> _readHeldKey;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private WindowsHotkeyService? _hotkeys;
    private bool _shuttingDown, _hotkeyFailureReported;
    private volatile bool _capturing;
    private volatile bool _capturingHotkey;
    private int _startHotkeyVk = 0x77, _hotkeyGeneration;
    private CancellationTokenSource? _holdSession;
    private string _triggerMode = "切换触发";
    private int _virtualKey=0x41;
    private string _mode="鼠标", _button="左键", _keyDisplay="A", _frequency="10", _hold="30", _repeats="0", _x="0", _y="0", _notice="就绪。开始前请确认目标窗口和紧急停止快捷键。", _hotkeyStatus="正在注册全局快捷键…";
    private bool _fixed;
    private string _maxDuration="0";
    private string _randomInterval="0";
    private string _burstCount="0", _burstRest="1000";
    private bool _isMacroPage;
    private string _lastClickMode = "鼠标";
    public string[] Modes { get; } = ["鼠标", "键盘"];
    public string[] Buttons { get; } = ["左键", "右键", "中键"];
    public string[] TriggerModes { get; } = ["切换触发", "按住触发"];
    public string TriggerMode { get=>_triggerMode; set { if (IsTaskActive() || Volatile.Read(ref _holdSession) is not null) { Notice="请先停止任务并松开触发键，再切换触发方式。"; PropertyChanged?.Invoke(this,new(nameof(TriggerMode))); return; } Set(ref _triggerMode,value); } }
    public bool IsCapturingHotkey => _capturingHotkey;
    public string StartHotkeyLabel => "F" + (_startHotkeyVk - 0x6F);
    public string HotkeyButtonText => _capturingHotkey ? "取消设置" : "设置快捷键";
    public string HotkeySidebar => StartHotkeyLabel + "   开始 / 暂停";
    public string Mode { get=>_mode; set { Set(ref _mode,value); if (value is "鼠标" or "键盘") _lastClickMode=value; } }
    public bool IsClickPage => !_isMacroPage;
    public bool IsMacroPage => _isMacroPage;
    public string PageTitle => _isMacroPage ? "宏引擎" : "连点引擎";
    public string PageSubtitle => _isMacroPage ? "编排顺序操作，让重复任务有序运行。" : "设定鼠标或键盘操作，实时掌控每一次执行。";
    public string ActiveTask => _lastMacro ? "宏引擎" : "连点引擎";
    public string Button { get=>_button; set=>Set(ref _button,value); }
    public string KeyDisplay { get=>_keyDisplay; private set=>Set(ref _keyDisplay,value); }
    public bool IsCapturing => _capturing;
    public string CaptureButtonText => _capturing ? "取消设置" : "设置按键";
    public string Frequency { get=>_frequency; set=>Set(ref _frequency,value); }
    public string Hold { get=>_hold; set=>Set(ref _hold,value); }
    public string Repeats { get=>_repeats; set=>Set(ref _repeats,value); }
    public string MaxDuration { get=>_maxDuration; set=>Set(ref _maxDuration,value); }
    public string RandomInterval { get=>_randomInterval; set=>Set(ref _randomInterval,value); }
    public string BurstCount { get=>_burstCount; set=>Set(ref _burstCount,value); }
    public string BurstRest { get=>_burstRest; set=>Set(ref _burstRest,value); }
    public string X { get=>_x; set=>Set(ref _x,value); }
    public string Y { get=>_y; set=>Set(ref _y,value); }
    public bool Fixed { get=>_fixed; set=>Set(ref _fixed,value); }
    public string Notice { get=>_notice; private set=>Set(ref _notice,value); }
    public string HotkeyStatus { get=>_hotkeyStatus; private set=>Set(ref _hotkeyStatus,value); }
    public string State { get; private set; } = "待机";
    public string Count { get; private set; } = "0";
    public string Elapsed { get; private set; } = "00:00:00";
    public string Average { get; private set; } = "0.0 / 秒";
    public string Recent { get; private set; } = "0.0 / 秒";
    private long _testClicks;
    public string TestClicks => $"测试区收到 {_testClicks:N0} 次点击";
    public ICommand TestClickCommand { get; }
    public ICommand ResetTestCommand { get; }
    public ICommand CaptureHotkeyCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand NavigateClickCommand { get; }
    public ICommand NavigateMacroCommand { get; }
    public ICommand ClickRunCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public MainViewModel(Dispatcher dispatcher, TaskCoordinator? engine = null, Func<int,bool>? readHeldKey = null)
    {
        _dispatcher=dispatcher;
        _engine=engine ?? new TaskCoordinator(new WindowsInputSink());
        _readHeldKey=readHeldKey ?? IsKeyDown;
        InitializeMacro();
        InitializePosition();
        Localization.Instance.PropertyChanged += OnLanguageChanged;
        StartCommand=new RelayCommand(StartOrResume); PauseCommand=new RelayCommand(Pause); StopCommand=new RelayCommand(()=>_ = StopAsync()); RetryCommand=new RelayCommand(Retry); CaptureCommand=new RelayCommand(ToggleCapture);
        NavigateClickCommand=new RelayCommand(GoClick); NavigateMacroCommand=new RelayCommand(GoMacro); ClickRunCommand=new RelayCommand(RunClick);
        TestClickCommand=new RelayCommand(()=> { _testClicks++; PropertyChanged?.Invoke(this,new(nameof(TestClicks))); });
        ResetTestCommand=new RelayCommand(()=> { _testClicks=0; PropertyChanged?.Invoke(this,new(nameof(TestClicks))); });
        CaptureHotkeyCommand=new RelayCommand(ToggleHotkeyCapture);
        try
        {
            _hotkeys=CreateHotkeyService(_startHotkeyVk);
            UpdateHotkeyStatus();
        }
        catch (Exception ex) { HotkeyStatus="全局快捷键注册失败；已禁止启动："+ex.Message; }
        _timer=new DispatcherTimer(TimeSpan.FromMilliseconds(150),DispatcherPriority.Background,(_,_)=>Refresh(),dispatcher); _timer.Start();
    }
    private void GoClick() { CancelCapture(); _isMacroPage=false; Mode=_lastClickMode; NotifyPage(); }
    private void GoMacro() { CancelCapture(); CancelPosition(); _isMacroPage=true; Mode="宏"; NotifyPage(); }
    private void NotifyPage() { foreach(var n in new[]{nameof(IsClickPage),nameof(IsMacroPage),nameof(PageTitle),nameof(PageSubtitle)}) PropertyChanged?.Invoke(this,new(n)); }
    private void RunClick()
    {
        if (IsActive(_engine.MacroSnapshot.State)) { Notice="请先停止当前宏任务。"; return; }
        if (Mode=="宏") Mode=_lastClickMode;
        StartOrResume();
    }
    private InputPlan MakePlan()
    {
        if (!double.TryParse(Frequency,NumberStyles.Float,CultureInfo.InvariantCulture,out var rate) && !double.TryParse(Frequency,NumberStyles.Float,CultureInfo.CurrentCulture,out rate)) throw new ArgumentException("频率请输入数字。");
        if (!int.TryParse(Hold,out int hold) || !long.TryParse(Repeats,out long repeats) || repeats<0) throw new ArgumentException("保持时间和次数请输入有效整数。");
        if (!double.IsFinite(rate) || rate is < 0.1 or > 50) throw new ArgumentException("频率须为 0.1–50 次每秒。");
        if (hold is < 1 or > 5000) throw new ArgumentException("保持时间须为 1–5000 毫秒。");
        if (repeats > 10000000) throw new ArgumentException("执行次数不能超过 10000000。");
        if (!int.TryParse(MaxDuration,out int maxDuration) || maxDuration < 0 || maxDuration > 86400) throw new ArgumentException("最长时间请输入 0 到 86400 秒的整数。");
        if (!int.TryParse(RandomInterval,out int randomInterval) || randomInterval < 0 || randomInterval > 50) throw new ArgumentException("随机间隔请输入 0 到 50 的整数百分比。");
        if (!int.TryParse(BurstCount,out int burstCount) || burstCount < 0 || burstCount > 1000 || !int.TryParse(BurstRest,out int burstRest) || burstRest < 1 || burstRest > 60000) throw new ArgumentException("爆发次数请输入 0–1000，休息时间请输入 1–60000 毫秒。");
        var kind = Mode=="键盘" ? InputKind.Key : Button switch { "左键"=>InputKind.MouseLeft, "右键"=>InputKind.MouseRight, "中键"=>InputKind.MouseMiddle, _=>throw new ArgumentException("请选择鼠标按钮。") };
        var vk = kind==InputKind.Key ? _virtualKey : 0;
        ScreenPoint? position=null;
        if (kind==InputKind.Key && vk==_startHotkeyVk) throw new ArgumentException("目标按键不能与开始快捷键相同。");
        if (Fixed && kind!=InputKind.Key) { if (!int.TryParse(X,out var x)||!int.TryParse(Y,out var y)) throw new ArgumentException("坐标请输入整数。"); position=new(x,y); }
        var plan=new InputPlan(kind,vk,rate,hold,repeats==0?null:repeats,position,MaximumDurationSeconds:maxDuration==0?null:maxDuration,RandomIntervalPercent:randomInterval,BurstCount:burstCount,BurstRestMilliseconds:burstRest); plan.Validate(); return plan;
    }
    private void StartOrResume()
    {
        if (_shuttingDown) return;
        try
        {
            if (_capturing || _capturingHotkey || _recording || _positionCapture is not null) throw new InvalidOperationException("请先完成或取消按键设置、位置拾取，并停止录制。");
            if (Volatile.Read(ref _holdSession) is not null) throw new InvalidOperationException("请先松开按住触发的快捷键。");
            if (_hotkeys is null || !_hotkeys.IsRunning || _hotkeys.LastError is not null) throw new InvalidOperationException("全局停止快捷键不可用，不能启动任务。");
            var s=CurrentState();
            if (s==EngineState.Paused) { _engine.Resume(); Notice="任务已恢复。"; }
            else if (s==EngineState.Running) Notice="任务已在运行；请先暂停或停止。";
            else { if (Mode=="宏") { _engine.StartMacro(BuildMacroPlan()); _lastMacro=true; } else { _engine.StartClick(MakePlan()); _lastMacro=false; } Notice="任务已启动；修改后的参数将在下次启动时生效。"; }
        }
        catch(Exception ex) { Notice="无法启动："+ex.Message; }
        Refresh();
    }
    private void Pause() { if (_engine.Pause()) Notice="已请求暂停；正在保持的输入会先释放。"; Refresh(); }
    private void ToggleFromHotkey() { if (_capturing || _capturingHotkey || _recording) return; if (CurrentState()==EngineState.Running) Pause(); else StartOrResume(); }
    private void ToggleCapture()
    {
        if (_capturing) { CancelCapture(); return; }
        if (_recording) { Notice="请先停止录制，再单独设置按键。"; return; }
        if (_capturingHotkey) { Notice="请先完成快捷键设置。"; return; }
        if (_positionCapture is not null) { Notice="请先完成或取消位置拾取。"; return; }
        if (IsTaskActive())
        { Notice="请先停止任务，再设置按键。"; return; }
        if (Mode!="宏") Mode="键盘"; _capturing=true; PropertyChanged?.Invoke(this,new(nameof(CaptureButtonText)));
        Notice="请在本窗口按下一个键。F8、F11、F12 保留给控制功能；再次点击按钮可取消。";
    }
    public void CancelCapture()
    {
        if (!_capturing) return;
        _capturing=false; PropertyChanged?.Invoke(this,new(nameof(CaptureButtonText)));
        Notice="按键设置已取消。";
    }
    public void CaptureKey(KeyEventArgs e)
    {
        if (!_capturing || e.IsRepeat) return;
        var key=e.Key==System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key is System.Windows.Input.Key.F8 or System.Windows.Input.Key.F11 or System.Windows.Input.Key.F12 || KeyInterop.VirtualKeyFromKey(key)==_startHotkeyVk)
        { Notice="当前启动快捷键以及 F8、F11、F12 保留给控制功能，请选择其他按键。"; return; }
        if (key is System.Windows.Input.Key.ImeProcessed or System.Windows.Input.Key.DeadCharProcessed or System.Windows.Input.Key.None)
        { Notice="无法识别此按键，请切换到直接键盘输入后重试。"; return; }
        bool isModifier=key is System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin;
        if (!isModifier && Keyboard.Modifiers!=ModifierKeys.None)
        { Notice="当前只支持单键，请松开修饰键后重新按下目标键。"; return; }
        int vk=KeyInterop.VirtualKeyFromKey(key);
        if (vk is < 1 or > 254) { Notice="无法识别此按键，请选择其他按键。"; return; }
        _virtualKey=vk;
        KeyDisplay=key switch { System.Windows.Input.Key.Space=>"空格",System.Windows.Input.Key.Return=>"Enter",System.Windows.Input.Key.Up=>"方向上",System.Windows.Input.Key.Down=>"方向下",System.Windows.Input.Key.Left=>"方向左",System.Windows.Input.Key.Right=>"方向右",_ when key is >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 => ((int)key-(int)System.Windows.Input.Key.D0).ToString(CultureInfo.InvariantCulture),_=>key.ToString() };
        _capturing=false; PropertyChanged?.Invoke(this,new(nameof(CaptureButtonText)));
        Notice="已设置按键："+KeyDisplay+"。";
    }
    public async Task StopAsync()
    {
        Interlocked.Increment(ref _hotkeyGeneration);
        CancelHoldSession();
        if (!_dispatcher.HasShutdownStarted) _ = _dispatcher.BeginInvoke(new Action(CancelPosition));
        if (_recording && !_dispatcher.HasShutdownStarted) _ = _dispatcher.BeginInvoke(new Action(()=>StopRecording("录制已停止。")));
        try { await _engine.StopAsync().ConfigureAwait(false); UpdateOnUi("已停止。", false); }
        catch(Exception ex) { UpdateOnUi("停止或释放失败："+ex.Message,true); }
    }
    private void Retry() { try { _engine.RetryPendingRelease(); Notice="已尝试释放待处理的输入。"; } catch(Exception ex) { Notice="重试释放失败："+ex.Message; } Refresh(); }
    private void UpdateOnUi(string message,bool error) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(new Action(()=>{ Notice=message; Refresh(); })); }
    private void Refresh()
    {
        if (_shuttingDown) return;
        if (_recording) RecordingExpired();
        if (!_hotkeyFailureReported && _hotkeys is not null && (!_hotkeys.IsRunning || _hotkeys.LastError is not null)) { _hotkeyFailureReported=true; HotkeyStatus="快捷键服务异常，任务正在停止。"; _ = StopAsync(); }
        var s=_engine.ClickSnapshot; var m=_engine.MacroSnapshot; var state=CurrentState();
        State=state switch { EngineState.Idle when !_lastMacro && s.TimedOut=>"已到时",EngineState.Idle=>"待机",EngineState.Running=>"运行中",EngineState.Paused=>_engine.HasPendingRelease ? "暂停中" : "已暂停",EngineState.Stopping=>"停止中",_=>"故障" };
        Count=(_lastMacro?m.CompletedSteps:s.Completed).ToString("N0",CultureInfo.CurrentCulture); Elapsed=_lastMacro?"—":s.ActiveTime.ToString(@"hh\:mm\:ss"); Average=_lastMacro?"—":s.AverageFrequencyHz.ToString("F1",CultureInfo.CurrentCulture)+" / 秒";
        Recent=_lastMacro?"—":s.RecentFrequencyHz.ToString("F1",CultureInfo.CurrentCulture)+" / 秒";
        MacroProgress=_lastMacro && m.StepIndex>=0 ? $"第 {m.Cycle} 轮 · 步骤 {m.StepIndex+1}" : "—";
        string? error=_lastMacro?m.Error:s.Error;
        if (state==EngineState.Faulted && !string.IsNullOrEmpty(error)) Notice="任务异常："+error+(_engine.HasPendingRelease?"；请点击重试释放。":"");
        foreach(var n in new[]{nameof(State),nameof(Count),nameof(Elapsed),nameof(Average),nameof(Recent),nameof(MacroProgress),nameof(ActiveTask)}) PropertyChanged?.Invoke(this,new(n));
    }
    public async Task ShutdownAsync()
    {
        _shuttingDown=true; _timer.Stop(); Localization.Instance.PropertyChanged -= OnLanguageChanged; CancelPosition(); CancelHotkeyCapture(); CancelHoldSession(); StopRecording("录制已停止。");
        try { _hotkeys?.Dispose(); } finally { await _engine.DisposeAsync(); }
    }
    private void Set<T>(ref T field,T value,[CallerMemberName] string? name=null) { if (EqualityComparer<T>.Default.Equals(field,value)) return; field=value; PropertyChanged?.Invoke(this,new(name)); }
    private sealed class RelayCommand(Action action) : ICommand { public event EventHandler? CanExecuteChanged { add {} remove {} } public bool CanExecute(object? p)=>true; public void Execute(object? p)=>action(); }
}
