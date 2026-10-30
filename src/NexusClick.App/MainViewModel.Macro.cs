using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using NexusClick.Engine;

namespace NexusClick.App;

public sealed partial class MainViewModel
{
    private MacroDraft _draft = new();
    private readonly MacroStore _macroStore = new();
    private Guid _macroId = Guid.NewGuid();
    private bool _lastMacro;
    private bool _macroNameIsGenerated = true;
    private int _macroSelected = -1;
    private string _macroName = Localization.Instance.Translate("未命名宏"), _macroStepType = "等待", _macroDuration = "100", _macroRepeats = "1", _macroTimeout = "300";
    public string[] MacroStepTypes { get; } = ["按键", "鼠标左键", "鼠标右键", "鼠标中键", "等待"];
    public ObservableCollection<string> MacroSteps { get; } = new();
    public int MacroSelected { get => _macroSelected; set => Set(ref _macroSelected, value); }
    public string MacroName { get => _macroName; set { if (value == _macroName) return; _macroNameIsGenerated = false; Set(ref _macroName, value); } }
    private void SetDefaultMacroName()
    {
        _macroNameIsGenerated = true;
        Set(ref _macroName, Localization.Instance.Translate("未命名宏"), nameof(MacroName));
    }
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Localization.Language) && _macroNameIsGenerated) SetDefaultMacroName();
    }
    public string MacroStepType { get => _macroStepType; set => Set(ref _macroStepType, value); }
    public string MacroDuration { get => _macroDuration; set => Set(ref _macroDuration, value); }
    public string MacroRepeats { get => _macroRepeats; set => Set(ref _macroRepeats, value); }
    public string MacroTimeout { get => _macroTimeout; set => Set(ref _macroTimeout, value); }
    public string MacroProgress { get; private set; } = "—";
    public ICommand MacroAddCommand { get; private set; } = null!;
    public ICommand MacroReplaceCommand { get; private set; } = null!;
    public ICommand MacroRemoveCommand { get; private set; } = null!;
    public ICommand MacroDuplicateCommand { get; private set; } = null!;
    public ICommand MacroUpCommand { get; private set; } = null!;
    public ICommand MacroDownCommand { get; private set; } = null!;
    public ICommand MacroNewCommand { get; private set; } = null!;
    public ICommand MacroSaveCommand { get; private set; } = null!;
    public ICommand MacroLoadCommand { get; private set; } = null!;
    public ICommand MacroRunCommand { get; private set; } = null!;
    public ICommand RecordCommand { get; private set; } = null!;
    private void InitializeMacro()
    {
        MacroAddCommand = new RelayCommand(()=>EditMacro(()=>ApplyDraft(_draft.Insert(_draft.Steps.Count, MakeMacroStep()), _draft.Steps.Count)));
        MacroReplaceCommand = new RelayCommand(()=>EditMacro(()=>ApplyDraft(_draft.Replace(MacroSelected, MakeMacroStep()), MacroSelected)));
        MacroRemoveCommand = new RelayCommand(()=>EditMacro(()=>ApplyDraft(_draft.Remove(MacroSelected), Math.Min(MacroSelected, _draft.Steps.Count-2))));
        MacroDuplicateCommand = new RelayCommand(()=>EditMacro(()=>ApplyDraft(_draft.Duplicate(MacroSelected), MacroSelected+1)));
        MacroUpCommand = new RelayCommand(()=>EditMacro(()=>ApplyDraft(_draft.Move(MacroSelected, MacroSelected-1), MacroSelected-1)));
        MacroDownCommand = new RelayCommand(()=>EditMacro(()=>ApplyDraft(_draft.Move(MacroSelected, MacroSelected+1), MacroSelected+1)));
        MacroNewCommand = new RelayCommand(()=>EditMacro(()=>{ _macroId=Guid.NewGuid(); SetDefaultMacroName(); ApplyDraft(new MacroDraft(), -1); }));
        MacroSaveCommand = new RelayCommand(()=>_ = SaveMacroAsync());
        MacroLoadCommand = new RelayCommand(()=>_ = LoadMacroAsync());
        MacroRunCommand = new RelayCommand(RunMacro);
        RecordCommand = new RelayCommand(ToggleRecording);
    }
    private void RunMacro()
    {
        if (IsActive(_engine.ClickSnapshot.State)) { Notice="请先停止当前连点任务。"; return; }
        Mode="宏"; StartOrResume();
    }
    private static bool IsActive(EngineState state) => state is EngineState.Running or EngineState.Paused or EngineState.Stopping;
    private bool IsTaskActive() => IsActive(_engine.ClickSnapshot.State) || IsActive(_engine.MacroSnapshot.State);
    private EngineState CurrentState()
    {
        var click=_engine.ClickSnapshot.State; var macro=_engine.MacroSnapshot.State;
        if (IsActive(macro)) return macro;
        if (IsActive(click)) return click;
        return _lastMacro ? macro : click;
    }
    private void EditMacro(Action action)
    {
        try { if (IsTaskActive() || _recording) throw new InvalidOperationException("请先停止任务或录制，再编辑宏。"); action(); Notice="宏已更新；导入不会自动执行。"; }
        catch(ArgumentOutOfRangeException) { Notice="无法编辑宏：请选择有效步骤，并检查是否已到列表边界。"; }
        catch(Exception ex) { Notice="无法编辑宏："+ex.Message; }
    }
    private MacroStep MakeMacroStep()
    {
        if (!int.TryParse(MacroDuration, out var ms)) throw new ArgumentException("步骤时长请输入整数毫秒。");
        var kind=MacroStepType switch { "按键"=>MacroStepKind.Key,"鼠标左键"=>MacroStepKind.MouseLeft,"鼠标右键"=>MacroStepKind.MouseRight,"鼠标中键"=>MacroStepKind.MouseMiddle,"等待"=>MacroStepKind.Wait,_=>throw new ArgumentException("请选择步骤类型。") };
        if (ms < 1 || ms > (kind==MacroStepKind.Wait ? 60000 : 5000)) throw new ArgumentException(kind==MacroStepKind.Wait ? "等待时间须为 1–60000 毫秒。" : "按键或鼠标保持时间须为 1–5000 毫秒。");
        var step=new MacroStep(kind, kind==MacroStepKind.Key ? _virtualKey : 0, ms); step.Validate(); return step;
    }
    private void ApplyDraft(MacroDraft draft, int selected)
    {
        _draft=draft; MacroSteps.Clear();
        for(int i=0;i<draft.Steps.Count;i++)
        {
            var s=draft.Steps[i];
            var label=s.Kind switch { MacroStepKind.Key=>$"按键 {FormatMacroKey(s.VirtualKey)}",MacroStepKind.MouseLeft=>"鼠标左键",MacroStepKind.MouseRight=>"鼠标右键",MacroStepKind.MouseMiddle=>"鼠标中键",_=>"等待" };
            MacroSteps.Add($"{i+1:00}  ·  {label}  ·  {s.DurationMilliseconds} 毫秒");
        }
        MacroSelected=selected;
    }
    private static string FormatMacroKey(int virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        0x20 => "空格", 0x0D => "Enter", 0x25 => "方向左", 0x26 => "方向上", 0x27 => "方向右", 0x28 => "方向下",
        _ => KeyInterop.KeyFromVirtualKey(virtualKey).ToString()
    };
    private MacroPlan BuildMacroPlan()
    {
        if (!int.TryParse(MacroRepeats,out var repeats) || !int.TryParse(MacroTimeout,out var seconds)) throw new ArgumentException("宏执行次数和最长时长必须为整数。");
        if (_draft.Steps.Count==0) throw new InvalidDataException("请先添加至少一个宏步骤。");
        if (repeats is < 1 or > 10000 || (long)_draft.Steps.Count*repeats>100000) throw new InvalidDataException("宏轮数须为 1–10000，且总执行步骤不能超过 100000。");
        if (seconds is < 1 or > 86400) throw new InvalidDataException("宏最长时间须为 1–86400 秒。");
        var plan=_draft.ToPlan(repeats, seconds);
        // Injecting F8 or an emergency key can trigger our global controls.
        if (plan.Steps.Any(s=>s.Kind==MacroStepKind.Key && s.VirtualKey is 0x77 or 0x7A or 0x7B)) throw new InvalidDataException("宏中不能使用保留键 F8、F11 或 F12。");
        if (plan.Steps.Any(s=>s.Kind==MacroStepKind.Key && s.VirtualKey==_startHotkeyVk)) throw new InvalidDataException("宏不能使用当前启动快捷键。");
        return plan;
    }
    private async Task SaveMacroAsync()
    {
        try
        {
            if (_recording) throw new InvalidOperationException("请先停止录制再保存。");
            if (string.IsNullOrWhiteSpace(MacroName) || MacroName.Length>80 || MacroName.Any(char.IsControl)) throw new ArgumentException("宏名称须为 1–80 个非控制字符。");
            var doc=new MacroDocument(1,_macroId,MacroName,BuildMacroPlan()); doc.Validate();
            var dialog=new SaveFileDialog { Filter=Localization.Instance.Translate("宏文件")+" (*.json)|*.json", FileName="dianjing-macro.json", AddExtension=true, DefaultExt=".json" };
            if (dialog.ShowDialog()!=true) return;
            await _macroStore.SaveAsync(dialog.FileName,doc); Notice="宏已保存。";
        }
        catch(Exception ex) { Notice="保存失败："+ex.Message; }
    }
    private async Task LoadMacroAsync()
    {
        try
        {
            if (IsTaskActive() || _recording) throw new InvalidOperationException("请先停止任务或录制，再加载宏。");
            var dialog=new OpenFileDialog { Filter=Localization.Instance.Translate("宏文件")+" (*.json)|*.json", CheckFileExists=true };
            if (dialog.ShowDialog()!=true) return;
            var doc=await _macroStore.LoadAsync(dialog.FileName);
            if (IsTaskActive() || _recording) throw new InvalidOperationException("任务或录制已启动，未替换当前宏。");
            // Reject reserved hotkeys before changing the current draft.
            if (doc.Plan.Steps.Any(s=>s.Kind==MacroStepKind.Key && s.VirtualKey is 0x77 or 0x7A or 0x7B)) throw new InvalidDataException("宏含保留键 F8、F11 或 F12。");
            var draft=new MacroDraft(doc.Plan.Steps);
            _macroId=doc.Id; MacroName=doc.Name; MacroRepeats=doc.Plan.RepeatCount.ToString(); MacroTimeout=doc.Plan.MaximumWallSeconds.ToString(); ApplyDraft(draft,-1); GoMacro();
            Notice="宏已加载；不会自动执行。";
        }
        catch(InvalidDataException) { Notice="加载失败：宏文件格式或参数无效，当前宏未更改。"; }
        catch(Exception ex) { Notice="加载失败："+ex.Message; }
    }
}
