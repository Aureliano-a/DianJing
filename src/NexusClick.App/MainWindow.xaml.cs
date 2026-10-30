using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
namespace NexusClick.App;
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _closing, _closed;
    public MainWindow() { InitializeComponent(); _vm = new MainViewModel(Dispatcher); DataContext = _vm; }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12) { e.Handled = true; _vm.CancelCapture(); _vm.CancelHotkeyCapture(); _vm.StopRecording("录制已停止。"); _ = _vm.StopAsync(); return; }
        if (_vm.IsRecording) { e.Handled = true; _vm.RecordKeyDown(e); return; }
        if (_vm.IsCapturingHotkey) { e.Handled = true; _vm.CaptureHotkey(e); return; }
        if (_vm.IsCapturing) { e.Handled = true; _vm.CaptureKey(e); }
    }
    private void Window_KeyUp(object sender, KeyEventArgs e) { if (_vm.IsRecording) { e.Handled = true; _vm.RecordKeyUp(e); } }
    private void Window_Deactivated(object? sender, EventArgs e) { _vm.CancelCapture(); _vm.CancelHotkeyCapture(); _vm.StopRecording("窗口失去焦点，录制已停止；未完成的按键不会保存。"); }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true; if (_closing) return; _closing = true;
        try { await _vm.ShutdownAsync(); }
        catch (Exception ex) { var loc=Localization.Instance; MessageBox.Show(loc.Translate("停止或释放输入时发生错误：") + loc.Translate(ex.Message) + "\n" + loc.Translate("请检查键盘和鼠标状态。"), loc.Translate("关闭点境"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        _closed = true; Close();
    }
}

