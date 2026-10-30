using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NexusClick.Engine;

/// <summary>Windows SendInput adapter. Windows integrity boundaries still apply.</summary>
public sealed class WindowsInputSink : IInputSink
{
    private bool _downAttempted;
    public void Down(InputPlan plan) => Send(plan, false);
    public void Up(InputPlan plan) => Send(plan, true);
    private void Send(InputPlan plan, bool up)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (up && !_downAttempted) return;
        if (!up)
        {
            plan.Validate();
            if (plan.FixedPosition is { } point) MoveToPhysicalPoint(point);
        }
        Input input;
        if (plan.Kind == InputKind.Key)
        {
            input = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = checked((ushort)plan.VirtualKey), Flags = KeyboardInputFlags.ForVirtualKey(plan.VirtualKey, up, plan.ExtendedKey) } } };
        }
        else
        {
            uint flags = (plan.Kind, up) switch
            {
                (InputKind.MouseLeft, false) => 0x0002, (InputKind.MouseLeft, true) => 0x0004,
                (InputKind.MouseRight, false) => 0x0008, (InputKind.MouseRight, true) => 0x0010,
                (InputKind.MouseMiddle, false) => 0x0020, (InputKind.MouseMiddle, true) => 0x0040,
                _ => throw new ArgumentOutOfRangeException(nameof(plan))
            };
            input = new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = flags } } };
        }
        if (!up) _downAttempted = true;
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput failed; target may have a higher integrity level.");
        if (up) _downAttempted = false;
    }
    private static void MoveToPhysicalPoint(ScreenPoint point)
    {
        // Per-monitor DPI awareness makes the virtual desktop metrics and coordinates physical pixels.
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previous == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set DPI context.");
        try
        {
            var bounds = new DesktopBounds(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
            var normalized = DesktopCoordinates.Normalize(point, bounds);
            if (MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, 0) == IntPtr.Zero)
                throw new ArgumentOutOfRangeException(nameof(point), "Point is not on an attached monitor.");
            var move = new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = normalized.X, Y = normalized.Y, Flags = 0xC001 } } };
            if (SendInput(1, [move], Marshal.SizeOf<Input>()) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput could not move the pointer.");
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public HardwareInput Hardware; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HardwareInput { public uint Message; public ushort ParamLow, ParamHigh; }
}
