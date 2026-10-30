# DianJing · 点境

**A Windows mouse and keyboard auto-clicker and macro tool**

English · [简体中文](README.zh-CN.md)


DianJing is a local Windows 11 x64 application for mouse and keyboard repetition and sequential macros. Its interface features an animated space background and a rotating Earth. The project is under active development; issues and contributions are welcome.

### Features

- Repeat left, right, or middle mouse clicks or a custom single key, with rate, hold time, random interval, burst rest, count, and duration controls.
- Follow the cursor or pick a fixed screen position; start tasks with a toggle or by holding a hotkey.
- Record single keys in the app window or arrange key, mouse-click, and wait steps manually; save and load macros as JSON.
- **F8** starts or pauses by default; **Ctrl + Shift + F11** is the global emergency stop. **F12** stops within the app window. The start key can be changed to F1–F10 for the current session.
- Interface languages: Simplified Chinese, English, Japanese, Spanish, and Russian. Decorative animations can be switched off.

### Install and use

Installer releases: [GitHub Releases](https://github.com/Aureliano-a/DianJing/releases). Install using the Windows 11 x64 installer. When updating, close the running app and select the existing installation folder. DianJing runs locally and requires no account or subscription. See the [user guide](使用说明.md) for instructions in five languages.

Background keyboard recording, mouse recording, key combinations, a profile center, and analytics charts are not implemented yet. Recording is limited to the app window and stops on focus loss. Windows scheduling, permissions, and target applications affect actual input; millisecond accuracy is not guaranteed. Real target-app input, multi-monitor coordinates, and high-DPI behavior need further device testing.

### Build and test

Requires Windows and the .NET 10 SDK:

```powershell
dotnet build src/NexusClick.App/NexusClick.App.csproj -c Release
dotnet run --project tests/NexusClick.Engine.Tests/NexusClick.Engine.Tests.csproj -c Release
dotnet run --project tests/NexusClick.App.Tests/NexusClick.App.Tests.csproj -c Release
```
 See [development notes](DEVELOPMENT.md) for the project layout and installer build procedure.


## License

Source code is distributed under the [MIT License](LICENSE). The Earth texture is a NASA historical image with separate attribution and usage conditions; see [third-party notices](THIRD-PARTY-NOTICES.md). The generated space background is 1672 × 941 pixels, not native 4K.
