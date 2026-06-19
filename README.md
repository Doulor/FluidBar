# FluidBar

> Windows 上的灵动岛体验 — 将 macOS 动态岛风格的通知和信息显示带到 Windows 桌面

FluidBar 是一个轻量级的 Windows 桌面应用，使用 WPF 构建，在屏幕顶部以优雅的药丸形状（Pill）浮窗实时展示系统状态、媒体播放、剪贴板内容和应用通知。它的设计灵感来源于 iOS 的 Dynamic Island，通过玻璃拟态（Glassmorphism）、弹簧动画和渐变光效，为 Windows 用户带来精致的交互体验。

---

### 按住ctrl+Alt左键灵动岛本体即可打开配置面板

---

## 功能预览

### 媒体播放

实时显示当前播放的音乐信息，包括专辑封面、歌曲名、艺术家、播放状态和音频波形动画。支持歌词显示（酷狗歌词 API）和播放控制：

![媒体播放](README%20ICON/Media.gif)

### 系统状态监控

当触发 Caps Lock、Num Lock 等锁定键时，灵动岛会平滑展开显示当前状态：

![锁定键状态](README%20ICON/CAPS.png)

支持的系统监控包括：音量、亮度、电池、输入法、锁定键、网络连接、USB 设备插拔、蓝牙设备、时钟等。

### Windows 通知

将系统通知以灵动岛的形式展示，显示应用图标、标题和通知内容：

![系统通知](README%20ICON/Notify.png)

### 时钟显示

在灵动岛上显示当前时间和日期：

![时钟](README%20ICON/time.png)

### 等等功能...

---

## 核心特性

- **10 个系统监控器** — 音量、亮度、电池、时钟、输入法、锁定键、网络、USB、蓝牙、通知
- **4 个内置插件** — 剪贴板、媒体播放、Agent 状态、Windows 通知
- **多岛堆叠** — 多个事件同时触发时自动堆叠显示，支持独立窗口模式
- **悬停卡片** — 鼠标悬停时展开详细信息，支持弹簧物理动画
- **媒体控制** — 播放/暂停、上一曲/下一曲，实时进度条和音频波形
- **酷狗歌词** — 自动获取匹配歌词并以滚动动画展示
- **浏览器媒体检测** — 自动识别浏览器中正在播放的媒体内容
- **Agent 事件** — 接收 Claude Code / Codex 等本地 hook 事件的灵动岛提醒
- **插件系统** — 开放式源码插件架构，可轻松扩展新功能
- **主题感知** — 自动跟随 Windows 深色/浅色主题切换
- **全局快捷键** — 支持自定义快捷键快速隐藏灵动岛
- **低资源占用** — 最小依赖，纯 P/Invoke 调用，单文件发布

---

## 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10 (19041+) 或 Windows 11 |
| 运行时 | [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| 权限 | 普通用户（亮度控制需要 DDC/CI 支持的显示器） |

---

## 快速开始

### 方式一：下载发布版本

从 [Releases](../../releases) 页面下载最新版本，解压后运行 `FluidBar.exe`。

### 方式二：从源码构建

```bash
# 克隆仓库
git clone https://github.com/Doulor/FluidBar.git
cd FluidBar

# 构建
dotnet build -c Release

# 运行
dotnet run
```

### 方式三：开发模式（热重载）

```bash
# Windows
.\.Aenable-dev.bat

# 或手动执行
dotnet watch run
```

### 方式四：发布单文件

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

---

## 架构概览

FluidBar 采用 **Event Bus** 架构，所有数据源（系统监控器和插件）通过发布 `IslandEvent` 对象到中央 `EventBus`，UI 层订阅并渲染到灵动岛窗口。

```
┌─────────────────────────────────────────────────────┐
│                     EventBus                        │
│                  (事件总线中心)                       │
├──────────────┬──────────────┬───────────────────────┤
│  System      │  Plugin      │  Island               │
│  Monitors    │  Manager     │  Presentation         │
│  (10个)      │  (4个插件)    │  (视图映射)           │
├──────────────┴──────────────┴───────────────────────┤
│              MainWindow (WPF UI)                    │
│         灵动岛渲染 · 动画 · 悬停卡片                  │
└─────────────────────────────────────────────────────┘
```

### 关键设计模式

- **Event Bus / Pub-Sub** — 数据源与 UI 完全解耦
- **Policy/Strategy** — 动画策略、显示策略、媒体选择策略等静态策略类
- **Record Types** — 使用 C# record 实现不可变数据模型
- **Spring Physics** — 自定义弹簧物理动画系统
- **Debounced Settings** — 防抖设置保存，批量处理快速操作

---

## 项目结构

```
FluidBar/
├── App.xaml / App.xaml.cs          # 应用入口，生命周期管理
├── MainWindow.xaml / .cs           # 灵动岛主窗口
├── SettingsWindow.xaml / .cs       # 设置界面（4个标签页）
├── Settings.cs                     # 设置模型与持久化
├── EventSystem.cs                  # EventBus、IslandEvent 核心
├── IslandPresentation.cs           # 视图映射与动画配置
├── IslandSnapshotWindow.cs         # 多岛快照窗口
├── PluginManager.cs                # 插件生命周期管理
├── PluginCatalog.cs                # 插件目录（catalog.json）
├── Monitors/                       # 系统监控器
│   ├── VolumeMonitor.cs            # 音量监控
│   ├── BrightnessMonitor.cs        # 亮度监控（DDC/CI + WMI）
│   ├── BatteryMonitor.cs           # 电池状态
│   ├── ClockMonitor.cs             # 时钟
│   ├── InputMethodMonitor.cs       # 输入法
│   ├── LockKeyMonitor.cs           # 锁定键
│   ├── NetworkMonitor.cs           # 网络连接
│   ├── UsbMonitor.cs               # USB 设备
│   ├── BluetoothMonitor.cs         # 蓝牙设备
│   └── NotificationMonitor.cs      # Windows 通知
├── Plugins/                        # 插件
│   ├── Clipboard/                  # 剪贴板插件
│   ├── Media/                      # 媒体播放插件（12个子模块）
│   ├── AgentStatus/                # Agent 状态插件
│   ├── Notifications/              # 通知插件
│   ├── Template/                   # 插件开发模板
│   └── catalog.json                # 插件目录
├── FluidBar.Tests/                 # 测试项目
└── FluidBar.csproj                 # 项目配置
```

---

## 插件开发

FluidBar 使用源码级别的插件系统（非运行时 DLL 加载），所有插件代码经过 PR 审核后随应用一同发布。

### 快速开始

1. 在 `Plugins/` 下创建新目录
2. 实现 `IIslandPlugin` 接口
3. 在 `Plugins/catalog.json` 中添加目录条目
4. 在 `FluidBar.Tests/` 中添加测试
5. 提交 PR

详细文档请参考 [Plugins/README.md](Plugins/README.md) 和 [Plugins/Template/](Plugins/Template/)。

---

## 依赖

| 依赖 | 用途 |
|------|------|
| .NET 10 SDK | 构建和运行时 |
| System.Management | WMI 查询（亮度、蓝牙、USB） |
| WinRT APIs | 媒体会话（GSMTC）、通知监听 |
| Win32 P/Invoke | 系统状态查询、剪贴板监听、窗口图标提取 |

整个项目仅依赖一个 NuGet 包（`System.Management`），其余均使用 .NET 内置 API 或 Win32 P/Invoke。

---

## 构建与测试

```bash
# 构建
dotnet build

# 运行测试
dotnet run --project FluidBar.Tests/FluidBar.Tests.csproj

# 验证构建（CI 用）
dotnet build -c CodexVerify

# 发布单文件
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
```

---

## 许可证

本项目为开源项目，详见 [LICENSE](LICENSE) 文件。

---

## 贡献

欢迎提交 Issue 和 Pull Request。如果你有新的插件想法，请先在 Issues 中讨论。

---

<p align="center">
  <sub>Built with WPF & .NET 10 &middot; Designed for Windows</sub>
</p>
