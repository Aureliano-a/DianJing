using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using NexusClick.Engine;

// This opt-in test sends F24 and clicks only after verifying this test window is foreground.
// It never reads or records ordinary keyboard input.
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var previous = GetForegroundWindow();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Title = "NEXUS CLICK 输入验证", Width = 420, Height = 240, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false, Topmost = true, Content = "输入验证进行中，请不要操作鼠标或切换窗口。完成后窗口会自动关闭。" };
        int keys = 0, clicks = 0;
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.F24) { keys++; e.Handled = true; } };
        window.MouseLeftButtonDown += (_, _) => clicks++;
        window.Loaded += async (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            GetCursorPos(out var originalCursor);
            Point injectedCursor = default;
            bool moved = false;
            try
            {
                window.Activate(); SetForegroundWindow(hwnd); window.Focus();
                await Task.Delay(200);
                if (GetForegroundWindow() != hwnd) throw new InvalidOperationException("Test window could not become foreground; no input sent.");
                var sink = new TestSink(hwnd);
                await using var engine = new InputEngine(sink);
                engine.Start(new InputPlan(InputKind.Key, 0x87, 10, 30, 3));
                await WaitForIdle(engine);
                await Task.Delay(150);
                if (keys != 3) throw new InvalidOperationException($"Expected 3 F24 events, received {keys}.");
                var position = window.PointToScreen(new System.Windows.Point(window.ActualWidth / 2, window.ActualHeight / 2));
                injectedCursor = new Point { X = (int)position.X, Y = (int)position.Y };
                if (GetForegroundWindow() != hwnd || WindowFromPoint(injectedCursor) != hwnd) throw new InvalidOperationException("Test window is not the foreground target; no mouse input sent.");
                if (!SetCursorPos(injectedCursor.X, injectedCursor.Y)) throw new InvalidOperationException("Cannot position test cursor.");
                moved = true; sink.ExpectedCursor = injectedCursor;
                engine.Start(new InputPlan(InputKind.MouseLeft, 0, 10, 30, 3));
                await WaitForIdle(engine);
                await Task.Delay(150);
                if (clicks != 3) throw new InvalidOperationException($"Expected 3 mouse events, received {clicks}.");
                // Begin away from the target so the fixed-coordinate path must move the pointer.
                if (GetForegroundWindow() != hwnd || !SetCursorPos(injectedCursor.X - 30, injectedCursor.Y)) throw new InvalidOperationException("Cannot prepare fixed-position test.");
                engine.Start(new InputPlan(InputKind.MouseLeft, 0, 10, 30, 1, new ScreenPoint(injectedCursor.X, injectedCursor.Y)));
                await WaitForIdle(engine);
                await Task.Delay(150);
                if (clicks != 4 || !GetCursorPos(out var fixedResult) || fixedResult.X != injectedCursor.X || fixedResult.Y != injectedCursor.Y) throw new InvalidOperationException("Fixed-position click failed.");
                Console.WriteLine($"PASS: self-window received {keys} F24 presses, {clicks} mouse clicks, and a fixed-position move.");
                Environment.ExitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); Environment.ExitCode = 1; }
            finally
            {
                // Do not override a cursor the user has moved during the test.
                if (moved && GetCursorPos(out var current) && current.X == injectedCursor.X && current.Y == injectedCursor.Y) SetCursorPos(originalCursor.X, originalCursor.Y);
                if (GetForegroundWindow() == hwnd && previous != IntPtr.Zero) SetForegroundWindow(previous);
                window.Close(); app.Shutdown();
            }
        };
        app.Run(window);
        return Environment.ExitCode;
    }
    private static async Task WaitForIdle(InputEngine engine)
    {
        for (int i = 0; i < 200; i++)
        {
            var s = engine.Snapshot;
            if (s.State == EngineState.Faulted) throw new InvalidOperationException(s.Error);
            if (s.State == EngineState.Idle) { await engine.StopAsync(); return; }
            await Task.Delay(20);
        }
        await engine.StopAsync(); throw new TimeoutException("Input test timed out.");
    }
    private sealed class TestSink(IntPtr window) : IInputSink
    {
        private readonly WindowsInputSink _sender = new();
        private bool _attempted;
        public Point? ExpectedCursor { get; set; }
        public void Down(InputPlan plan)
        {
            _attempted = false;
            if (GetForegroundWindow() != window) throw new InvalidOperationException("Test window lost focus.");
            if (plan.Kind != InputKind.Key && (ExpectedCursor is not { } target || WindowFromPoint(target) != window || (plan.FixedPosition is null && (!GetCursorPos(out var p) || p.X != target.X || p.Y != target.Y)))) throw new InvalidOperationException("Mouse target changed.");
            _attempted = true; _sender.Down(plan);
        }
        public void Up(InputPlan plan) { if (_attempted) _sender.Up(plan); _attempted = false; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
}
