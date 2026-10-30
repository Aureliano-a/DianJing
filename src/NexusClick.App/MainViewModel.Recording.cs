using System.Diagnostics;
using System.Windows.Input;
using NexusClick.Engine;

namespace NexusClick.App;

public sealed partial class MainViewModel
{
    private volatile bool _recording;
    private long _recordStarted, _lastRecordedUp, _pendingDown, _pendingGap;
    private int? _pendingVirtualKey;
    public bool IsRecording => _recording;
    public string RecordButtonText => _recording ? "■  停止录制" : "●  录制按键";
    public string RecordingIndicator => _recording ? "●  正在录制 · 仅本窗口" : "仅本窗口录制，切换窗口自动停止";
    private void ToggleRecording()
    {
        if (_recording) { StopRecording("录制已停止；已完成的步骤保留在列表中。"); return; }
        if (_capturing || _capturingHotkey || _positionCapture is not null || IsTaskActive()) { Notice="请先停止任务或完成按键设置和位置拾取，再开始录制。"; return; }
        if (_draft.Steps.Count >= 1000) { Notice="宏已达到 1000 步上限。"; return; }
        _pendingVirtualKey=null; _lastRecordedUp=0; _recordStarted=Stopwatch.GetTimestamp(); _recording=true;
        NotifyRecording(); Notice="正在录制本窗口内的顺序单键；切换窗口将停止，修饰键和组合键不录制。";
    }
    public void StopRecording(string message)
    {
        if (!_recording) return;
        _recording=false; _pendingVirtualKey=null; _lastRecordedUp=0; NotifyRecording(); Notice=message;
    }
    private void NotifyRecording()
    {
        PropertyChanged?.Invoke(this,new(nameof(RecordButtonText)));
        PropertyChanged?.Invoke(this,new(nameof(RecordingIndicator)));
    }
    private bool RecordingExpired()
    {
        if (!_recording || Stopwatch.GetElapsedTime(_recordStarted).TotalSeconds < 60) return false;
        StopRecording("录制达到 60 秒上限；未释放的按键未保存。"); return true;
    }
    private static System.Windows.Input.Key EventKey(KeyEventArgs e) => e.Key==System.Windows.Input.Key.System ? e.SystemKey : e.Key;
    private static bool ModifierKey(System.Windows.Input.Key key) => key is System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin;
    public void RecordKeyDown(KeyEventArgs e)
    {
        if (!_recording || RecordingExpired() || e.IsRepeat) return;
        var key=EventKey(e);
        if (key is System.Windows.Input.Key.F8 or System.Windows.Input.Key.F11 or System.Windows.Input.Key.F12 || KeyInterop.VirtualKeyFromKey(key)==_startHotkeyVk) { Notice="控制快捷键不会录入宏。"; return; }
        if (ModifierKey(key))
        {
            if (_pendingVirtualKey is not null) StopRecording("检测到组合键；录制已停止，未完成按键未保存。");
            else Notice="暂不录制修饰键或组合键。";
            return;
        }
        if (Keyboard.Modifiers!=ModifierKeys.None) { Notice="检测到组合键：当前只录制不含修饰键的单键。"; return; }
        if (_pendingVirtualKey is not null) { StopRecording("检测到按键重叠；录制已停止，未完成按键未保存。"); return; }
        int vk=KeyInterop.VirtualKeyFromKey(key);
        if (vk is < 1 or > 254 || key is System.Windows.Input.Key.ImeProcessed or System.Windows.Input.Key.DeadCharProcessed)
        { Notice="无法识别此按键，未录入。"; return; }
        var now=Stopwatch.GetTimestamp();
        _pendingGap=_lastRecordedUp==0 ? 0 : Math.Max(0,(long)Stopwatch.GetElapsedTime(_lastRecordedUp,now).TotalMilliseconds);
        _pendingDown=now; _pendingVirtualKey=vk;
    }
    public void RecordKeyUp(KeyEventArgs e)
    {
        if (!_recording || RecordingExpired() || _pendingVirtualKey is not int vk) return;
        var key=EventKey(e); if (KeyInterop.VirtualKeyFromKey(key)!=vk) return;
        var now=Stopwatch.GetTimestamp();
        long held=Math.Max(1,(long)Stopwatch.GetElapsedTime(_pendingDown,now).TotalMilliseconds);
        if (held>5000 || _pendingGap>60000) { StopRecording("按键或等待时间超过支持范围，未保存此按键。"); return; }
        int needed=_pendingGap>0 ? 2 : 1;
        if (_draft.Steps.Count+needed>1000) { StopRecording("宏已达到 1000 步上限，未保存此按键。"); return; }
        var draft=_draft;
        if (_pendingGap>0) draft=draft.Insert(draft.Steps.Count,new MacroStep(MacroStepKind.Wait,DurationMilliseconds:(int)_pendingGap));
        draft=draft.Insert(draft.Steps.Count,new MacroStep(MacroStepKind.Key,vk,(int)held));
        ApplyDraft(draft,draft.Steps.Count-1); _pendingVirtualKey=null; _lastRecordedUp=now;
        Notice="正在录制本窗口内的顺序单键 · 已记录 "+_draft.Steps.Count+" 步。";
    }
}
