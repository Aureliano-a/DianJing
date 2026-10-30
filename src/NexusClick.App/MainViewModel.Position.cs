using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace NexusClick.App;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _positionCapture;
    public ICommand PickPositionCommand { get; private set; } = null!;
    public string PickPositionText => _positionCapture is null ? "拾取位置" : "取消拾取";

    private void InitializePosition() => PickPositionCommand = new RelayCommand(() => _ = PickPositionAsync());

    private async Task PickPositionAsync()
    {
        if (_positionCapture is not null) { CancelPosition(); Notice="已取消位置拾取。"; return; }
        if (IsTaskActive() || _capturing || _capturingHotkey || _recording || _shuttingDown) { Notice="请先停止任务并结束按键设置或录制。"; return; }
        var capture = new CancellationTokenSource();
        _positionCapture = capture;
        PropertyChanged?.Invoke(this, new(nameof(PickPositionText)));
        try
        {
            for (int seconds = 3; seconds > 0; seconds--)
            {
                Notice=$"请将鼠标移到目标位置，{seconds} 秒后拾取；再次点击可取消。";
                await Task.Delay(1000, capture.Token);
            }
            capture.Token.ThrowIfCancellationRequested();
            if (!GetPhysicalCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error());
            X=point.X.ToString(); Y=point.Y.ToString(); Fixed=true; Mode="鼠标";
            Notice=$"已拾取屏幕物理坐标：X {point.X}，Y {point.Y}。请确认位置后再启动。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice="位置拾取失败：" + ex.Message; }
        finally
        {
            if (ReferenceEquals(_positionCapture, capture)) { _positionCapture=null; PropertyChanged?.Invoke(this, new(nameof(PickPositionText))); }
            capture.Dispose();
        }
    }

    private void CancelPosition() { _positionCapture?.Cancel(); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out NativePoint point);
}
