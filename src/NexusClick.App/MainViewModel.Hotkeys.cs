using System.Runtime.InteropServices;
using System.Windows.Input;
using NexusClick.Engine;

namespace NexusClick.App;

public sealed partial class MainViewModel
{
    private WindowsHotkeyService CreateHotkeyService(int startVk) => new(
        new(HotkeyModifiers.None, (uint)startVk), new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7A),
        OnStartHotkey, () => { _ = StopAsync(); if (_recording && !_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(new Action(() => StopRecording("紧急停止：录制已结束。"))); });

    private void UpdateHotkeyStatus() => HotkeyStatus=$"全局快捷键：{StartHotkeyLabel} 启动 / 暂停；Ctrl + Shift + F11 紧急停止。F12 仅在本窗口有效。";

    private void ToggleHotkeyCapture()
    {
        if (_capturingHotkey) { CancelHotkeyCapture(); return; }
        if (_capturing || _recording || _positionCapture is not null || IsTaskActive()) { Notice="请先停止任务，并结束录制、按键设置和位置拾取。"; return; }
        _capturingHotkey=true; PropertyChanged?.Invoke(this,new(nameof(HotkeyButtonText)));
        Notice="请在本窗口按 F1–F10 设置启动快捷键；F11 和 F12 保留给停止功能。";
    }

    public void CancelHotkeyCapture()
    {
        if (!_capturingHotkey) return;
        _capturingHotkey=false; PropertyChanged?.Invoke(this,new(nameof(HotkeyButtonText)));
    }

    public void CaptureHotkey(KeyEventArgs e)
    {
        if (!_capturingHotkey || e.IsRepeat) return;
        var key=e.Key==Key.System ? e.SystemKey : e.Key;
        if (key < Key.F1 || key > Key.F10 || Keyboard.Modifiers != ModifierKeys.None) { Notice="请选择不带修饰键的 F1–F10。"; return; }
        int vk=KeyInterop.VirtualKeyFromKey(key);
        if (vk==_virtualKey) { Notice="启动快捷键不能与当前连按按键相同。"; return; }
        if (_draft.Steps.Any(s => s.Kind == MacroStepKind.Key && s.VirtualKey == vk)) { Notice="启动快捷键不能与当前宏步骤中的按键相同。"; return; }
        try
        {
            if (_hotkeys is null) _hotkeys=CreateHotkeyService(vk);
            else _hotkeys.ChangeStartHotkey(new(HotkeyModifiers.None, (uint)vk));
            _startHotkeyVk=vk; _hotkeyFailureReported=false; CancelHotkeyCapture();
            PropertyChanged?.Invoke(this,new(nameof(StartHotkeyLabel)));
            PropertyChanged?.Invoke(this,new(nameof(HotkeySidebar)));
            UpdateHotkeyStatus(); Notice=$"启动快捷键已设置为 {StartHotkeyLabel}。";
        }
        catch (Exception ex) { Notice="快捷键设置失败："+ex.Message; }
    }

    private void OnStartHotkey()
    {
        if (_capturing || _capturingHotkey || _recording || _dispatcher.HasShutdownStarted) return;
        var generation=Volatile.Read(ref _hotkeyGeneration);
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_shuttingDown || generation!=Volatile.Read(ref _hotkeyGeneration)) return;
            if (TriggerMode=="按住触发") StartWhileHeld(); else ToggleFromHotkey();
        }));
    }

    private void StartWhileHeld()
    {
        if (IsMacroPage || Mode=="宏") { Notice="按住触发仅适用于连点引擎。"; return; }
        if (IsTaskActive() || Volatile.Read(ref _holdSession) is not null) { Notice="请先停止当前任务。"; return; }
        var vk=_startHotkeyVk;
        if (!_readHeldKey(vk)) return;
        StartOrResume();
        if (_engine.ClickSnapshot.State != EngineState.Running) return;
        var session=new CancellationTokenSource();
        Volatile.Write(ref _holdSession,session);
        _ = MonitorHeldKey(session,vk);
        Notice=$"按住 {StartHotkeyLabel} 运行；松开后停止。";
    }

    private async Task MonitorHeldKey(CancellationTokenSource session, int vk)
    {
        try
        {
            while (true)
            {
                await Task.Delay(25,session.Token).ConfigureAwait(false);
                if (!_readHeldKey(vk)) break;
            }
            if (!session.IsCancellationRequested)
            {
                Interlocked.Increment(ref _hotkeyGeneration);
                await _engine.StopAsync().ConfigureAwait(false);
                UpdateOnUi("已松开快捷键，任务已停止。", false);
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try { await _engine.StopAsync().ConfigureAwait(false); } catch { }
            UpdateOnUi("按住触发异常，任务已请求停止："+ex.Message,true);
        }
        finally
        {
            Interlocked.CompareExchange(ref _holdSession,null,session);
            session.Dispose();
        }
    }

    private void CancelHoldSession()
    {
        try { Volatile.Read(ref _holdSession)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
