using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NexusClick.Engine;

[Flags]
public enum HotkeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public void Validate()
    {
        if ((Modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows)) != 0 || VirtualKey is < 1 or > 254)
            throw new ArgumentOutOfRangeException(nameof(VirtualKey), "Invalid key or modifiers.");
    }
}

/// <summary>
/// Owns two registered global hotkeys on a dedicated Windows message-loop thread.
/// Callbacks run on that thread and must return promptly. Registration is all-or-nothing.
/// </summary>
public sealed class WindowsHotkeyService : IDisposable
{
    private const uint WmHotkey = 0x0312, WmQuit = 0x0012, WmChangeStart = 0x8001, NoRepeat = 0x4000;
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Hotkey _startPause;
    private readonly Hotkey _emergency;
    private readonly object _changeLock = new();
    private Hotkey _pendingStart;
    private TaskCompletionSource<Exception?>? _pendingChange;
    private readonly Action _onStartPause, _onEmergency;
    private uint _threadId;
    private int _disposed;
    private Exception? _lastError;
    public Exception? LastError => Volatile.Read(ref _lastError);
    public bool IsRunning => Volatile.Read(ref _disposed) == 0 && _thread.IsAlive;
    public void ChangeStartHotkey(Hotkey replacement)
    {
        replacement.Validate();
        if (replacement == _emergency) throw new ArgumentException("The two hotkeys must be different.");
        lock (_changeLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (replacement == _startPause) return;
            var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingStart = replacement; Volatile.Write(ref _pendingChange, completion);
            if (!PostThreadMessage(_threadId, WmChangeStart, 0, 0)) { Volatile.Write(ref _pendingChange, null); throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not update the hotkey."); }
            var error = completion.Task.GetAwaiter().GetResult();
            Volatile.Write(ref _pendingChange, null);
            if (error is not null) throw error;
        }
    }

    public WindowsHotkeyService(Hotkey startPause, Hotkey emergency, Action onStartPause, Action onEmergency)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        startPause.Validate(); emergency.Validate();
        if (startPause == emergency) throw new ArgumentException("The two hotkeys must be different.");
        _startPause = startPause; _emergency = emergency;
        _onStartPause = onStartPause ?? throw new ArgumentNullException(nameof(onStartPause));
        _onEmergency = onEmergency ?? throw new ArgumentNullException(nameof(onEmergency));
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "NEXUS global hotkeys" };
        _thread.Start();
        try { _ready.Task.GetAwaiter().GetResult(); }
        catch { _thread.Join(); throw; }
    }
    private void MessageLoop()
    {
        bool registeredEmergency = false, registeredStart = false;
        int startId = 2;
        try
        {
            _threadId = GetCurrentThreadId();
            // Ensure the thread queue exists before Dispose can post WM_QUIT.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            if (!RegisterHotKey(IntPtr.Zero, 1, (uint)_emergency.Modifiers | NoRepeat, _emergency.VirtualKey))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to register the emergency hotkey.");
            registeredEmergency = true;
            if (!RegisterHotKey(IntPtr.Zero, 2, (uint)_startPause.Modifiers | NoRepeat, _startPause.VirtualKey))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to register the start/pause hotkey.");
            registeredStart = true;
            _ready.TrySetResult();
            while (true)
            {
                var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Hotkey message loop failed.");
                if (message.Code == WmHotkey)
                {
                    try { if (message.WParam == 1) _onEmergency(); else if (message.WParam == (nuint)startId) _onStartPause(); }
                    catch (Exception ex) { Volatile.Write(ref _lastError, ex); }
                }
                else if (message.Code == WmChangeStart)
                {
                    var pending = Volatile.Read(ref _pendingChange);
                    int newId = startId == 2 ? 3 : 2;
                    if (!RegisterHotKey(IntPtr.Zero, newId, (uint)_pendingStart.Modifiers | NoRepeat, _pendingStart.VirtualKey))
                        pending?.TrySetResult(new Win32Exception(Marshal.GetLastWin32Error(), "快捷键已被占用，原快捷键仍然有效。"));
                    else
                    {
                        if (!UnregisterHotKey(IntPtr.Zero, startId))
                        {
                            var error = new Win32Exception(Marshal.GetLastWin32Error(), "无法释放旧快捷键。");
                            UnregisterHotKey(IntPtr.Zero, newId);
                            pending?.TrySetResult(error);
                        }
                        else { startId = newId; _startPause = _pendingStart; pending?.TrySetResult(null); }
                    }
                }
            }
        }
        catch (Exception ex) { Volatile.Write(ref _lastError, ex); _ready.TrySetException(ex); Volatile.Read(ref _pendingChange)?.TrySetResult(ex); }
        finally
        {
            Volatile.Read(ref _pendingChange)?.TrySetResult(new InvalidOperationException("快捷键服务已停止。"));
            if (registeredStart) UnregisterHotKey(IntPtr.Zero, startId);
            if (registeredEmergency) UnregisterHotKey(IntPtr.Zero, 1);
        }
    }
    public void Dispose()
    {
        lock (_changeLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (!PostThreadMessage(_threadId, WmQuit, 0, 0) && _thread.IsAlive)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not stop the hotkey message loop.");
        }
        if (Thread.CurrentThread != _thread) _thread.Join();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr HWnd; public uint Code; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}

