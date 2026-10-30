using System.Reflection;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusClick.App;
using NexusClick.Engine;
using Localization = NexusClick.App.Localization;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        Environment.SetEnvironmentVariable("DIANJING_DISABLE_LANGUAGE_PERSISTENCE", "1");
        var app = new App(); app.InitializeComponent(); app.Startup -= app.StartMainWindow; app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var parameters = new HwndSourceParameters("NEXUS key capture test") { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) };
        using var source = new HwndSource(parameters);
        source.RootVisual = new Border();
        var testSink = new TestInputSink();
        int held = 0;
        var vm = new MainViewModel(Dispatcher.CurrentDispatcher, new TaskCoordinator(testSink), _ => Volatile.Read(ref held) != 0);
        try
        {
            void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            var planet = new PlanetArtwork();
            planet.Measure(new Size(240, 180)); planet.Arrange(new Rect(0, 0, 240, 180)); planet.UpdateLayout();
            var bitmap = new RenderTargetBitmap(240, 180, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(planet);
            var pixels = new byte[240 * 180 * 4]; bitmap.CopyPixels(pixels, 240 * 4, 0);
            Assert(pixels[(90 * 240 + 120) * 4 + 3] > 0, "planet artwork did not render");
            var rotation = (System.Windows.Media.Media3D.AxisAngleRotation3D)typeof(PlanetArtwork).GetField("_rotation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(planet)!;
            var renderFrame = typeof(PlanetArtwork).GetMethod("OnRendering", BindingFlags.Instance | BindingFlags.NonPublic)!;
            double initialAngle = rotation.Angle;
            renderFrame.Invoke(planet, new object?[] { null, EventArgs.Empty }); Thread.Sleep(45);
            renderFrame.Invoke(planet, new object?[] { null, EventArgs.Empty });
            Assert(rotation.Angle > initialAngle, "earth did not advance between animation frames");
            var previewPath = Environment.GetEnvironmentVariable("NEXUS_PLANET_PREVIEW");
            if (!string.IsNullOrWhiteSpace(previewPath)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(previewPath); encoder.Save(file); }
            var stars = new Starfield(); stars.IsAnimated = false;
            Assert(!stars.IsAnimated, "star animation could not be disabled");
            stars.Measure(new Size(240, 180)); stars.Arrange(new Rect(0, 0, 240, 180)); stars.UpdateLayout(); bitmap.Render(stars);
            stars.IsAnimated = true; Assert(stars.IsAnimated, "star animation could not be enabled");
            var scroll = new ScrollBar { Orientation = Orientation.Vertical, Style = (Style)app.FindResource(typeof(ScrollBar)) };
            scroll.ApplyTemplate();
            var track = (System.Windows.Controls.Primitives.Track)scroll.Template.FindName("PART_Track", scroll);
            Assert(track is not null && track.Orientation == Orientation.Vertical, "vertical scrollbar template failed");
            void Press(Key key) => vm.CaptureKey(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key));
            int VirtualKey() => (int)(typeof(MainViewModel).GetField("_virtualKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!);
            Assert(vm.IsClickPage && !vm.IsMacroPage, "initial navigation incorrect");
            vm.TestClickCommand.Execute(null); vm.TestClickCommand.Execute(null);
            Assert(vm.TestClicks.Contains("2"), "test area did not count clicks");
            vm.ResetTestCommand.Execute(null); Assert(vm.TestClicks.Contains("0"), "test area did not reset");
            vm.CaptureHotkeyCommand.Execute(null);
            vm.CaptureHotkey(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F12));
            Assert(vm.IsCapturingHotkey, "reserved hotkey was accepted");
            vm.CancelHotkeyCapture();
            var makePlan = typeof(MainViewModel).GetMethod("MakePlan", BindingFlags.Instance | BindingFlags.NonPublic)!;
            vm.MaxDuration="12";
            vm.RandomInterval="25";
            vm.BurstCount="3"; vm.BurstRest="800";
            var uiPlan = (InputPlan)makePlan.Invoke(vm, null)!;
            Assert(uiPlan.MaximumDurationSeconds == 12 && uiPlan.RandomIntervalPercent == 25 && uiPlan.BurstCount == 3 && uiPlan.BurstRestMilliseconds == 800, "duration, burst or random interval was not passed to the engine");
            vm.RandomInterval="51";
            try { makePlan.Invoke(vm, null); throw new InvalidOperationException("invalid random interval accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { }
            vm.RandomInterval="0";
            vm.BurstCount="0";
            vm.MaxDuration="86401";
            try { makePlan.Invoke(vm, null); throw new InvalidOperationException("invalid duration accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { }
            vm.MaxDuration="0";
            vm.Mode="键盘"; vm.NavigateMacroCommand.Execute(null);
            Assert(vm.IsMacroPage && !vm.IsClickPage && vm.Mode=="宏", "macro navigation incorrect");
            vm.NavigateClickCommand.Execute(null);
            Assert(vm.IsClickPage && !vm.IsMacroPage && vm.Mode=="键盘", "click navigation did not restore keyboard mode");
            vm.CaptureCommand.Execute(null); Assert(vm.IsCapturing && vm.Mode == "键盘", "capture did not start");
            vm.StartCommand.Execute(null); Assert(vm.IsCapturing && vm.Notice.Contains("先完成"), "start was not blocked during capture");
            Press(Key.D5); Assert(!vm.IsCapturing && vm.KeyDisplay == "5" && VirtualKey() == 0x35, "digit selection failed");
            vm.CaptureCommand.Execute(null); Press(Key.F6); Assert(!vm.IsCapturing && VirtualKey() == 0x75, "function key selection failed");
            vm.CaptureCommand.Execute(null); Press(Key.F8); Assert(vm.IsCapturing && VirtualKey() == 0x75, "reserved F8 accepted");
            Press(Key.F11); Press(Key.F12); Assert(vm.IsCapturing && VirtualKey() == 0x75, "reserved emergency key accepted");
            Press(Key.Divide); Assert(!vm.IsCapturing && VirtualKey() == 0x6F, "numpad divide selection failed");
            vm.CaptureCommand.Execute(null); vm.CancelCapture(); Assert(!vm.IsCapturing && VirtualKey() == 0x6F, "cancel changed selection");
            vm.Mode="宏"; vm.MacroStepType="等待"; vm.MacroDuration="15"; vm.MacroAddCommand.Execute(null);
            vm.MacroDuration="25"; vm.MacroAddCommand.Execute(null); vm.MacroSelected=1; vm.MacroUpCommand.Execute(null);
            Assert(vm.MacroSteps.Count==2 && vm.MacroSteps[0].Contains("25"), "macro move did not update order");
            vm.MacroDuplicateCommand.Execute(null); Assert(vm.MacroSteps.Count==3, "macro duplicate failed");
            vm.MacroRemoveCommand.Execute(null); Assert(vm.MacroSteps.Count==2, "macro remove failed");
            var build = typeof(MainViewModel).GetMethod("BuildMacroPlan", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var macro = (MacroPlan)build.Invoke(vm, null)!;
            Assert(macro.Steps[0].DurationMilliseconds==25 && macro.Steps[1].DurationMilliseconds==15, "macro plan did not preserve editor order");
            typeof(MainViewModel).GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, new MacroDraft());
            try { build.Invoke(vm, null); throw new InvalidOperationException("empty macro accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException && ex.InnerException.Message.Contains("至少一个")) { }
            typeof(MainViewModel).GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, new MacroDraft(new[]{new MacroStep(MacroStepKind.Key,0x77,20)}));
            try { build.Invoke(vm, null); throw new InvalidOperationException("reserved macro key accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
            vm.MacroNewCommand.Execute(null); vm.MacroStepType="等待"; vm.MacroDuration="1500"; vm.MacroTimeout="5"; vm.MacroAddCommand.Execute(null);
            if (vm.HotkeyStatus.Contains("全局快捷键："))
            {
                vm.MacroRunCommand.Execute(null);
                var coordinator=(TaskCoordinator)typeof(MainViewModel).GetField("_engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!;
                Assert(coordinator.MacroSnapshot.State==EngineState.Running, "macro did not start");
                vm.PauseCommand.Execute(null); Assert(coordinator.MacroSnapshot.State==EngineState.Paused, "macro did not pause");
                vm.NavigateClickCommand.Execute(null); vm.ClickRunCommand.Execute(null);
                Assert(coordinator.MacroSnapshot.State==EngineState.Paused && vm.Notice.Contains("停止当前宏"), "click start resumed a macro");
                vm.MacroAddCommand.Execute(null); Assert(vm.MacroSteps.Count==1, "editing allowed while macro paused");
                vm.MacroRunCommand.Execute(null); Assert(coordinator.MacroSnapshot.State==EngineState.Running, "macro did not resume");
                vm.StopAsync().GetAwaiter().GetResult(); Assert(coordinator.MacroSnapshot.State==EngineState.Idle, "macro did not stop");
            }
            else Console.WriteLine("SKIP: macro start/pause integration because hotkeys could not be registered");
            vm.MacroNewCommand.Execute(null); vm.NavigateMacroCommand.Execute(null); vm.RecordCommand.Execute(null);
            Assert(vm.IsRecording, "recording did not start");
            void Down(Key key) => vm.RecordKeyDown(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key));
            void Up(Key key) => vm.RecordKeyUp(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key));
            Down(Key.D2); Thread.Sleep(10); Up(Key.D2); Thread.Sleep(10); Down(Key.D3); Thread.Sleep(10); Up(Key.D3);
            Assert(vm.MacroSteps.Count==3 && vm.MacroSteps[0].Contains("按键 2") && vm.MacroSteps[1].Contains("等待") && vm.MacroSteps[2].Contains("按键 3"), "recorded sequence or timing missing");
            Down(Key.F11); Up(Key.F11); Assert(vm.MacroSteps.Count==3, "reserved key recorded");
            vm.StartCommand.Execute(null); Assert(vm.IsRecording && vm.Notice.Contains("停止录制"), "start allowed while recording");
            vm.MacroAddCommand.Execute(null); Assert(vm.MacroSteps.Count==3, "editing allowed while recording");
            Down(Key.A); vm.StopRecording("已停止"); Assert(vm.MacroSteps.Count==3 && !vm.IsRecording, "unfinished key was saved");
            vm.RecordCommand.Execute(null); Down(Key.A); Down(Key.B);
            Assert(!vm.IsRecording && vm.MacroSteps.Count==3, "overlapping keys were recorded as a sequence");
            vm.RecordCommand.Execute(null); Down(Key.A); Down(Key.LeftShift);
            Assert(!vm.IsRecording && vm.MacroSteps.Count==3, "overlapping modifier was recorded as a single key");
            vm.NavigateClickCommand.Execute(null); vm.PickPositionCommand.Execute(null);
            Assert(vm.PickPositionText == "取消拾取", "position pickup did not start");
            vm.StartCommand.Execute(null); Assert(vm.Notice.Contains("位置拾取"), "start allowed during position pickup");
            vm.PickPositionCommand.Execute(null);
            var capture = (CancellationTokenSource)typeof(MainViewModel).GetField("_positionCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            Assert(capture.IsCancellationRequested, "position pickup was not cancelled");
            void Pump(int milliseconds)
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(milliseconds) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue=false; };
                timer.Start(); Dispatcher.PushFrame(frame);
            }
            Pump(50);
            vm.PickPositionCommand.Execute(null); Pump(3300);
            Assert(vm.Fixed && int.TryParse(vm.X, out _) && int.TryParse(vm.Y, out _) && vm.PickPositionText == "拾取位置", "position pickup did not return physical coordinates");
            if (vm.HotkeyStatus.Contains("全局快捷键："))
            {
                vm.Fixed=false; vm.TriggerMode="按住触发"; Volatile.Write(ref held, 1);
                typeof(MainViewModel).GetMethod("OnStartHotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
                Pump(100);
                Assert(testSink.Downs > 0, "hold trigger did not start");
                Volatile.Write(ref held, 0); Pump(200);
                var coordinator=(TaskCoordinator)typeof(MainViewModel).GetField("_engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!;
                Assert(coordinator.ClickSnapshot.State==EngineState.Idle && testSink.Downs==testSink.Ups, "release did not stop and release input");
                Volatile.Write(ref held, 1);
                typeof(MainViewModel).GetMethod("OnStartHotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
                Pump(100); Assert(coordinator.ClickSnapshot.State==EngineState.Running, "second hold did not start");
                vm.StopAsync().GetAwaiter().GetResult(); var stoppedCount=testSink.Downs; Pump(150);
                Assert(coordinator.ClickSnapshot.State==EngineState.Idle && testSink.Downs==stoppedCount && testSink.Downs==testSink.Ups, "emergency stop did not end held task");
                typeof(MainViewModel).GetMethod("OnStartHotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
                vm.StopAsync().GetAwaiter().GetResult(); Pump(100);
                Assert(testSink.Downs==stoppedCount && coordinator.ClickSnapshot.State==EngineState.Idle, "queued hotkey restarted after stop");
                Volatile.Write(ref held, 0);
                vm.Repeats="1"; Volatile.Write(ref held, 1);
                typeof(MainViewModel).GetMethod("OnStartHotkey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
                Pump(150); Assert(coordinator.ClickSnapshot.State==EngineState.Idle, "finite held task did not finish");
                var finiteCount=testSink.Downs; vm.StartCommand.Execute(null); Pump(50);
                Assert(testSink.Downs==finiteCount && vm.Notice.Contains("松开"), "new task started before release of a completed held task");
                Volatile.Write(ref held, 0); Pump(100); vm.Repeats="0";
                vm.TriggerMode="切换触发";
            }
            else Console.WriteLine("SKIP: hold-trigger test because global hotkeys could not be registered");
            var window = new MainWindow();
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIANJING_LAYOUT_PREVIEW")))
                {
                    window.ShowActivated=false; window.ShowInTaskbar=false;
                    window.WindowStartupLocation=WindowStartupLocation.Manual;
                    window.Left=-10000; window.Top=-10000;
                    window.Show();
                }
                window.Measure(new Size(1120, 740)); window.Arrange(new Rect(0, 0, 1120, 740)); window.UpdateLayout();
                Pump(50);
                static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
                {
                    foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                    {
                        if (child is T item) yield return item;
                        foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
                    }
                }
                var tabs=LogicalDescendants<TabControl>(window).ToArray();
                Assert(tabs.Length == 2 && tabs[0].Items.Count == 4 && tabs[1].Items.Count == 2, "settings were not split into compact sections");
                var windowVm=(MainViewModel)window.DataContext;
                var languagePicker=LogicalDescendants<ComboBox>(window).Single(c => c.Items.Count==5 && c.Items[0] is ComboBoxItem);
                var translations=(Dictionary<string,string[]>)typeof(Localization).GetField("_strings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Localization.Instance)!;
                foreach (var (key, values) in translations)
                {
                    Assert(values.Length==4 && values.All(v=>!string.IsNullOrWhiteSpace(v)), "missing language for " + key);
                    var placeholders=System.Text.RegularExpressions.Regex.Matches(key, @"\{\d+\}").Select(m=>m.Value).Order().ToArray();
                    foreach (var value in values)
                        Assert(placeholders.SequenceEqual(System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}").Select(m=>m.Value).Order()), "translation placeholders do not match: " + key);
                }
                for (int language=1; language<=4; language++)
                {
                    languagePicker.SelectedIndex=language;
                    Pump(20);
                    Assert(Localization.Instance.Language==language, "language selector did not update the localization service");
                    Assert(Localization.Instance.Translate("基础设置") != "基础设置", "section label is not translated");
                    Assert(((TabItem)tabs[0].Items[0]).Header?.ToString() == Localization.Instance.Translate("基础设置"), "visible section label did not update");
                    Assert(window.Title == Localization.Instance.Translate("点境 · 键鼠自动化"), "window title did not update");
                    Assert(windowVm.Mode=="鼠标" && windowVm.Button=="左键", "language change altered engine settings");
                    Assert(windowVm.MacroName == Localization.Instance.Translate("未命名宏"), "generated macro name did not update with language");
                    foreach (var sample in new[] {
                        "第 2 轮 · 步骤 3", "请将鼠标移到目标位置，3 秒后拾取；再次点击可取消。",
                        "已拾取屏幕物理坐标：X -100，Y 80。请确认位置后再启动。", "启动快捷键已设置为 F7。",
                        "按住 F7 运行；松开后停止。", "正在录制本窗口内的顺序单键 · 已记录 4 步。",
                        "已设置按键：空格。", "01  ·  按键 空格  ·  20 毫秒",
                        "录制达到 60 秒上限；未释放的按键未保存。", "加载失败：宏文件格式或参数无效，当前宏未更改。",
                        "无法启动：A previous input release remains unresolved."
                    })
                        Assert(Localization.Instance.Translate(sample)!=sample, "message did not translate: " + sample);
                    Assert(Localization.Instance.Translate("已拾取屏幕物理坐标：X -100，Y 80。请确认位置后再启动。").Contains("-100"), "coordinates were lost in translation");
                    if (Environment.GetEnvironmentVariable("DIANJING_LAYOUT_PREVIEW") is string prefix && prefix.Length > 0)
                    {
                        window.Measure(new Size(1120,740)); window.Arrange(new Rect(0,0,1120,740)); window.UpdateLayout();
                        var screen=new RenderTargetBitmap(1120,740,96,96,PixelFormats.Pbgra32); screen.Render((Visual)window.Content);
                        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(screen));
                        using var output=File.Create(prefix + language + ".png"); png.Save(output);
                        windowVm.NavigateMacroCommand.Execute(null); Pump(20); window.UpdateLayout();
                        var macroScreen=new RenderTargetBitmap(1120,740,96,96,PixelFormats.Pbgra32); macroScreen.Render((Visual)window.Content);
                        var macroPng=new PngBitmapEncoder(); macroPng.Frames.Add(BitmapFrame.Create(macroScreen));
                        using var macroOutput=File.Create(prefix + "macro-" + language + ".png"); macroPng.Save(macroOutput);
                        windowVm.NavigateClickCommand.Execute(null); Pump(20);
                    }
                }
                windowVm.MacroName="My own macro";
                Localization.Instance.Language=0;
                Pump(20);
                Assert(((TabItem)tabs[0].Items[0]).Header?.ToString()=="基础设置", "Chinese label did not restore");
                Assert(windowVm.MacroName=="My own macro", "changing language overwrote a user-provided macro name");
                windowVm.MacroNewCommand.Execute(null);
                Assert(windowVm.MacroName=="未命名宏", "new macro did not use the current language");
                var toggle = (CheckBox)window.FindName("StarAnimationToggle");
                var backdrop = (Starfield)window.FindName("DeepSpaceBackdrop");
                var earth = (PlanetArtwork)window.FindName("EarthGlobe");
                var pulseButton=LogicalDescendants<Button>(window).First(button => Equals(button.Tag, "Pulse"));
                var fakeState=new PulseState();
                pulseButton.DataContext=fakeState;
                pulseButton.ApplyTemplate();
                var pulseRing=(Border?)pulseButton.Template.FindName("PulseRing", pulseButton) ?? throw new InvalidOperationException("glass pulse element missing");
                fakeState.State="运行中"; Pump(80);
                Assert(pulseRing.HasAnimatedProperties, "running state did not start the pulse");
                toggle.IsChecked = false;
                backdrop.GetBindingExpression(Starfield.IsAnimatedProperty)?.UpdateTarget();
                earth.GetBindingExpression(PlanetArtwork.IsAnimatedProperty)?.UpdateTarget();
                Pump(50);
                Assert(!backdrop.IsAnimated && !earth.IsAnimated, "window toggle did not disable space animations");
                Assert(!SpaceMotion.GetIsEnabled(pulseButton) && !pulseRing.HasAnimatedProperties, "animation switch did not stop the button pulse");
                toggle.IsChecked = true;
                backdrop.GetBindingExpression(Starfield.IsAnimatedProperty)?.UpdateTarget();
                earth.GetBindingExpression(PlanetArtwork.IsAnimatedProperty)?.UpdateTarget();
                Pump(50);
                Assert(backdrop.IsAnimated && earth.IsAnimated, "window toggle did not enable space animations");
                Assert(SpaceMotion.GetIsEnabled(pulseButton) && pulseRing.HasAnimatedProperties, "animation switch did not restore the pulse");
                fakeState.State="待机"; Pump(50);
                Assert(!pulseRing.HasAnimatedProperties, "button pulse continued after the task stopped");
                pulseButton.ClearValue(FrameworkElement.DataContextProperty);
            }
            finally { window.Hide(); ((MainViewModel)window.DataContext).ShutdownAsync().GetAwaiter().GetResult(); }
            Console.WriteLine("PASS: navigation, key selection, macro editor, recording and reserved hotkey validation");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally { vm.ShutdownAsync().GetAwaiter().GetResult(); }
    }
}

internal sealed class PulseState : System.ComponentModel.INotifyPropertyChanged
{
    private string _state = "待机";
    public string State { get => _state; set { _state=value; PropertyChanged?.Invoke(this, new(nameof(State))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class TestInputSink : IInputSink
{
    private int _downs, _ups;
    public int Downs => Volatile.Read(ref _downs);
    public int Ups => Volatile.Read(ref _ups);
    public void Down(InputPlan plan) => Interlocked.Increment(ref _downs);
    public void Up(InputPlan plan) => Interlocked.Increment(ref _ups);
}
