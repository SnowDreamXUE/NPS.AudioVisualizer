# NPS.AudioVisualizer

NotchPeninsula 的系统音频频谱插件，在灵动岛组件和详情页中显示实时频谱。项目以 C# 编写，当前版本为 **1.4.0**，插件 ID 为 `com.nps.audiovisualizer`。

## 功能

- 显示低频到高频共 5 段频谱，柱形带峰值保持效果。
- 提供柱形和双声道两种样式；双声道模式将左声道显示在上方、右声道显示在下方。
- 提供细、标准、粗三档柱宽，主题色、彩虹色、霓虹色三种配色，以及五档灵敏度。
- 支持无音频时自动隐藏：静音约 1.5 秒后组件收起并释放灵动岛占位，声音恢复后自动回来；未开启自动隐藏或声音监测不可用时保持显示。
- 详情页设置区改为「名称在左、选项在右」，候选项直接点击生效（不再下拉或循环切换），并提供一键重置全部设置。
- 详情页与插件设置中心双入口调整设置；设置保存在宿主提供的插件设置中，重启后保留。

## 音频与显示

普通柱形样式使用宿主每帧传入的 `WidgetFrame.Bars`。双声道样式需要区分左右声道，因此插件会在该样式启用期间额外打开 WASAPI Loopback 捕获，并分别分析左右声道。双声道捕获不可用时，会回退到单声道镜像显示。

自动隐藏开启后，插件会额外维持一条独立的声音活动监测（同样基于 WASAPI Loopback）：组件隐藏期间收不到宿主帧，恢复依赖该监测的采样峰值判定。监测启动失败时自动重试，期间组件保持显示。

灵动岛上的频谱可通过宿主显示设置启用和调整位置。点击组件可打开或关闭详情页；详情页中点击候选项直接切换样式、柱宽、配色和灵敏度，自动隐藏开关独占一行，重置按钮位于该行最右侧。

## 安装（无需本机编译）

安装需要一份与目标 NotchPeninsula 版本兼容的预编译 `NPS.AudioVisualizer.dll`，可直接从本仓库 `releases/` 目录获取；仓库同时提供全部源代码。

1. 从本仓库 `releases/` 目录取得 `NPS.AudioVisualizer.dll`，或使用提供构建能力的远程开发平台自行生成。
2. 将 DLL 放入 NotchPeninsula 主程序 exe 同级的 `plugins` 文件夹（例如 `C:\NotchPeninsula\plugins\`），或在宿主的 **设置 → 我的插件** 中导入 DLL。
3. 按宿主提示重启或重新加载插件。
4. 在 **设置 → 显示设置 → 显示内容** 中启用并调整组件显示。

本机不需要安装 SDK 或编译项目。若需要为新改动生成 DLL，请使用提供构建能力的远程开发平台或托管构建环境。

## 项目结构

| 文件 | 职责 |
| --- | --- |
| `src/AudioVisualizerPlugin.cs` | 插件入口、设置读取与迁移、立体声捕获生命周期 |
| `src/Core/VisualizerState.cs` | 共享状态与频谱平滑、峰值保持 |
| `src/Rendering/BarMetrics.cs` | 柱形粗细档位的尺寸表 |
| `src/Rendering/SpectrumPaint.cs` | 频段配色与柱形/辉光绘制 |
| `src/Rendering/PluginFont.cs` | 从宿主取得当前字体 |
| `src/Audio/StereoCapture.cs` | WASAPI Loopback 左右声道捕获、Goertzel 分析、双缓冲和声音活动监测 |
| `src/UI/VisualizerWidget.cs` | 灵动岛上的迷你频谱组件及其宽度测量（自动隐藏时宽度归零） |
| `src/UI/VisualizerDetailPage.cs` | 详情页频谱绘制、设置选项片 / 自动隐藏开关 / 重置交互 |
| `src/UI/VisualizerSettingsPage.cs` | 插件设置中心的选项和实时预览 |
| `NPS.AudioVisualizer.csproj` | 目标框架、宿主项目引用和编译依赖 |
| `releases/NPS.AudioVisualizer.dll` | 与当前版本对应的预编译产物 |
| `AI_DEVELOPMENT.md` | 供 AI 开发工具接手时阅读的项目约定和远程构建说明 |

## 开发环境说明

项目当前面向 `net10.0-windows10.0.19041.0`。项目文件位于仓库根目录，通过相对路径引用同级的 `NotchPeninsula` 宿主源码工程，因此单独检出本仓库并不能解析完整的编译引用。当前仓库没有包含 CI 构建工作流，预编译产物位于 `releases/` 目录；远程构建所需的目录布局、依赖和注意事项见 [AI_DEVELOPMENT.md](AI_DEVELOPMENT.md)。

本插件当前适配的宿主为 **NotchPeninsula 正式版 2.0.0**（GitHub release [NotchPeninsula 2.0](https://github.com/GEORGEWWWU/NotchPeninsula/releases/tag/NPS-v2.0)，对应 tag `NPS-v2.0`）。更新宿主兼容性前，请以该 tag 的宿主源码和插件 API 文档为准，不要默认 `main` 分支与已发布版本一致：

- [NotchPeninsula 宿主仓库](https://github.com/GEORGEWWWU/NotchPeninsula)
- [NPS-v2.0 的插件开发 API 文档](https://github.com/GEORGEWWWU/NotchPeninsula/blob/NPS-v2.0/NPS_PluginsAPI.md)

## 版本记录

- **1.4.0**：美化详情页设置区并新增无音频自动隐藏。设置区改为名称在左、候选项直接点击选中（无下拉框），四项设置两两一行；「无音频时自动隐藏」独占一行且与其他设置同宽，重置全部按钮固定在该行最右侧；面板高度 168→192。自动隐藏开启时静音约 1.5 秒后组件宽度归零、收回灵动岛占位，声音恢复即重新显示（基于 WASAPI 独立声音活动监测，监测不可用时保持显示）；新增重置全部，恢复柱形 / 标准 / 彩虹色 / ×1 / 关闭自动隐藏五项默认值；设置中心同步新增开关项。
- **1.3.2**：修复切换默认音频输出设备后双声道频谱冻结的问题——订阅 NAudio `RecordingStopped` 即时清零并重建（频谱落底而非卡在半空）、周期 tick 比对默认设备 ID 秒级跟随切换、捕获释放改为后台线程（不再阻塞宿主定时回调）、失败重试限速 3s→1.5s 且成功不计时；另在绘制层加 600ms 陈旧数据兜底，任何上游冻结最多表现为频谱落底。频谱算法、设置与持久化行为不变。
- **1.3.1**：适配 NotchPeninsula 正式版 2.0.0（tag `NPS-v2.0`）；作者信息更新为 SnowDream。2.0.0 插件 API 无破坏性变化（仅 `ReminderData` 新增可选 `Source` 字段，本插件未使用，行为不变），目标框架与宿主运行时依赖版本同 1.3.0。
- **1.3.0**：接入插件设置中心和实时预览；灵敏度设置迁移为索引存储，并保留旧值迁移。
- **1.2.0**：增加柱宽档位，调整灵敏度和配色选项。
- **1.1.0**：增加双声道显示。
- **1.0.0**：首个版本。

## 许可

本项目基于 [MIT License](LICENSE) 开源。
