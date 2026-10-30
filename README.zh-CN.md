# 点境 · DianJing

**Windows 键鼠连点与自动化宏工具**

简体中文 · [English](README.md)


点境是一款本地运行的 Windows 11 x64 工具，提供鼠标连点、键盘连按和顺序宏。界面以动态宇宙星空和自转地球为主题。项目处于持续开发阶段，欢迎反馈问题和参与改进。

### 主要功能

- 鼠标左键、右键、中键和自定义单键连点，支持频率、保持时间、随机间隔、爆发休息、次数和最长运行时间。
- 可跟随鼠标或拾取固定屏幕坐标；支持快捷键切换触发或按住触发。
- 宏支持在本窗口主动录制单键，或手动编排按键、鼠标点击和等待，支持 JSON 保存与加载。
- 默认 **F8** 开始／暂停，**Ctrl + Shift + F11** 全局紧急停止；**F12** 在点境窗口内停止。开始键可在当前运行期间更改为 F1–F10。
- 支持简体中文、English、日本語、Español、Русский；可关闭星空、地球和按钮脉冲动画。

### 安装与使用

安装包发布页：[GitHub Releases](https://github.com/Aureliano-a/DianJing/releases)。使用 Windows 11 x64 安装程序安装；更新时请关闭正在运行的点境并选择原安装目录。程序本地运行，无需账户或订阅。具体操作和限制见 [使用说明](使用说明.md)。

目前后台键盘录制、鼠标录制、多键组合、配置中心和分析曲线尚未实现。录制仅发生在点境窗口内，失去焦点会停止；实际速度受 Windows 调度、权限及目标程序影响，不保证毫秒精度。真实目标程序输入、多显示器坐标及高 DPI 仍需进一步实机验证。

### 构建与测试

需要 Windows 和 .NET 10 SDK：

```powershell
dotnet build src/NexusClick.App/NexusClick.App.csproj -c Release
dotnet run --project tests/NexusClick.Engine.Tests/NexusClick.Engine.Tests.csproj -c Release
dotnet run --project tests/NexusClick.App.Tests/NexusClick.App.Tests.csproj -c Release
```

安装程序构建方法和项目结构见 [开发说明](DEVELOPMENT.md)。


## 许可证

源代码采用 [MIT 许可证](LICENSE)。地球贴图为 NASA 历史影像，来源和使用条件见[第三方素材说明](THIRD-PARTY-NOTICES.md)。生成的宇宙背景尺寸为 1672 × 941，并非原生 4K。
