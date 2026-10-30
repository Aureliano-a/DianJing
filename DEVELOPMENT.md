# 点境 · DianJing

点境是一款面向 Windows 11 的本地键鼠连点和自动化宏工具，采用动态星空与地球主题界面。项目源码开放，包含 WPF 界面、独立输入引擎、安装脚本和测试；当前版本仍在开发中。

## 获取与安装

从项目的 GitHub Releases 下载 Windows x64 安装程序并运行，按提示选择安装位置。更新已有安装时请选择原安装目录。使用方法见 [使用说明](使用说明.md)。

## 许可证与素材

本项目源码使用 [MIT License](LICENSE)。地球影像另有来源及使用条件，详见 [第三方素材说明](THIRD-PARTY-NOTICES.md)。

## 项目结构

- `src/NexusClick.App`：WPF 深空界面、可关闭的动态星空和 NASA 贴图自转地球、全局热键接入、单键宏录制和手动宏界面。
- `src/NexusClick.Engine`：单键/单鼠标输入、任务协调、宏播放、JSON 存储与释放保护。
- `tests/NexusClick.Engine.Tests`：模拟发送器和存储测试。
- `tests/NexusClick.App.Tests`：WPF 按键选择、宏编辑及录制状态测试。
- `tests/NexusClick.Windows.Integration`：自有测试窗口的真实输入验证程序，尚未成功获得前台焦点。

## 构建与测试

需要 Windows 和 .NET 10 SDK。在此目录运行：

```powershell
dotnet build src/NexusClick.App/NexusClick.App.csproj -c Release
dotnet run --project tests/NexusClick.Engine.Tests/NexusClick.Engine.Tests.csproj -c Release
dotnet run --project tests/NexusClick.App.Tests/NexusClick.App.Tests.csproj -c Release
```

可将 WPF 程序发布为 Windows x64 自包含单文件。不要同时运行两个实例，否则全局热键可能冲突，程序会禁止启动任务。

## 功能与安全边界

单键或鼠标左/右/中键支持频率、保持时间、0–50% 随机周期、爆发休息、次数、最长运行时间和倒计时拾取固定坐标。随机变化以基础周期为准，最短目标周期 20 毫秒。最长时间包含暂停，到时停止并释放模拟输入。键盘自定义仅在用户主动设置且窗口激活时捕获一个键，不使用后台键盘录制。宏可在本窗口主动录制顺序单键和间隔等待；切换窗口自动停止，不能录制组合键或其他窗口输入。宏支持手动添加、替换、复制、删除、上下移动及 JSON 保存/加载，加载后不会自动执行；只支持键、鼠标和等待的顺序操作，有步数、次数与墙钟时间限制。运行过程中禁止编辑宏。F8/F11/F12 不能作为目标按键或宏步骤。

默认 F8 开始/暂停，支持本次运行内切换到 F1–F10，连点任务支持按住快捷键触发；Ctrl+Shift+F11 紧急停止，F12 仅窗口内停止。微软说明 F12 保留给调试器，不应通过 RegisterHotKey 注册为全局热键：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey

输入执行与 UI 分离；发送失败时报告，待释放输入未处理时禁止开启新任务。Windows 权限边界、调度和目标应用仍可能影响输入，不承诺毫秒精度。固定坐标、多显示器及高 DPI 尚未完成实机验证。

## 当前验证及未完成内容

模拟发送器测试、宏编辑顺序、录制事件和保留键验证通过。录制测试使用合成 WPF 事件，真实物理键盘录制尚需端到端验证。文件对话框及真实目标程序输入尚未完成端到端验证。测试窗口无法获得前台焦点时不会发送真实输入。

鼠标录制与拖拽、多键组合、双击/三击、配置中心、分析曲线、多主题切换和托盘设置仍未完成。不要将本基础版视为完整需求的最终发布版。



## 地球影像来源

地球使用 NASA/Goddard Space Flight Center Scientific Visualization Studio 的历史地球影像： https://svs.gsfc.nasa.gov/3615/ 。Blue Marble Next Generation 数据由 Reto Stockli (NASA/GSFC) 和 NASA Earth Observatory 提供。贴图非实时画面，云层不代表当前天气。




## 安装程序

安装脚本位于 installer/DianJing.nsi，使用 NSIS 3 Unicode 编译。先将 WPF 程序自包含发布至源码目录的同级 DianJing-Portable-v7 文件夹，发布时启用 `PublishSingleFile=true` 和 `IncludeNativeLibrariesForSelfExtract=true`；将生成的 NexusClick.exe 复制为 DianJing.exe，并将源码根目录的使用说明.md 复制至该文件夹；再运行 makensis 编译安装脚本。安装程序默认为当前用户安装，选择上级目录时自动添加 DianJing 文件夹。

宇宙背景由 OpenAI 内置 imagegen 生成，原始分辨率为 1672 × 941。界面叠加限帧动态星点及 NASA 历史地球贴图，运行中主按钮显示脉冲；关闭右上角的动画开关可同时停止星点、地球和按钮脉冲。

连点参数分为基础设置、快捷操作、运行限制和点击位置；宏编辑分为步骤编辑和运行与文件。程序与安装向导支持简体中文、英语、日语、西班牙语和俄语，语言选择保存在当前用户的本地设置。Windows 或第三方程序提供的原始错误详情可能使用其自身的语言。

品牌图标由 OpenAI 内置 imagegen 生成：深空底色、青蓝点击脉冲、紫色轨道和精密科技细节。


## 本次交互更新

支持按住和切换触发、F1–F10 启动快捷键选择（不持久化），以及按实际操作计数的近两秒频率和点击测试区。按住触发只适用于连点引擎；松开后停止，后台运行受 Windows 输入状态及调度影响。目标按键和宏步骤不能与当前启动快捷键冲突。


爆发模式每完成指定输入次数后，至少休息指定毫秒数；暂停期间休息计时继续，停止仍可中断休息。



安装程序会解析尚不存在的目标文件夹并在安装时创建；输入磁盘根目录时会自动使用该磁盘下的 DianJing 子文件夹。已安装到其他位置时，为保护现有安装，需使用现有位置更新或先卸载旧版。

