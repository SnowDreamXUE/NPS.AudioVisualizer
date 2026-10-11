# AI 开发交接说明

本文档供接手 `NPS.AudioVisualizer` 的 AI 开发工具阅读。它记录当前代码结构、宿主耦合点，以及不在用户本机编译的开发方式。

## 开发约束

- 用户希望在其他平台继续开发，**不要在用户本机执行 `dotnet build`、`dotnet test` 或其他本地编译流程**。
- 代码改动应以源码审阅为主。若任务需要编译产物，只能使用用户选定的远程开发平台或托管构建环境；说明构建是否成功以及产物位置。
- 仓库当前没有 CI 构建工作流、测试工程或预编译 DLL。不要假设代码已被构建或测试通过。
- 改动宿主接口前，先查阅宿主对应版本的官方 API 文档和源码。不要仅凭其他 NotchPeninsula 版本的接口猜测。
- 尽量保持插件仍以单个 `NPS.AudioVisualizer.dll` 分发；不要把宿主运行时已提供的依赖复制进插件目录。

## 项目与宿主

- 插件 ID：`com.nps.audiovisualizer`
- 当前插件版本：`1.3.0`
- 目标框架：`net10.0-windows10.0.19041.0`
- 插件入口：`AudioVisualizerPlugin`
- 宿主 API 命名空间：`NotchPeninsula.Plugins`
- 仓库原有 README 记录的目标宿主版本为 NPS v1.9.5。当前仓库没有包含宿主源码，变更兼容性前应核对目标版本的实际接口。

官方参考：

- [NotchPeninsula 仓库](https://github.com/GEORGEWWWU/NotchPeninsula)
- [NPS_PluginsAPI.md 插件开发说明](https://github.com/GEORGEWWWU/NotchPeninsula/blob/main/NPS_PluginsAPI.md)
- 若目标版本有对应 tag，应优先使用该 tag 的 API 文档和宿主源码，不要默认 `main` 与已发布版本完全一致。

## 当前实现结构

### `AudioVisualizerPlugin.cs`

实现 `INotchPlugin` 和 `IDisposable`。`Initialize` 读取持久化设置、订阅 `SettingsChanged`、注册岛组件及插件设置页，并通过 `ScheduleRefresh` 周期性启停立体声捕获。`Dispose` 退订事件、释放计划任务和捕获资源。

`VisualizerState` 保存 UI 共用状态、增益档位、平滑后的频段值和峰值。岛组件与详情页共用状态，`Feed` 会按时间去重，避免同一帧因两个绘制入口而重复推进平滑值。

### `VisualizerWidget.cs`

实现 `IWidget`，负责灵动岛上的迷你频谱。组件宽度由柱宽档位决定；粗细变化后需要调用 `IPluginHost.InvalidateWidgetLayout()`，让宿主重新测量宽度。左键操作打开或关闭详情页。

### `VisualizerDetailPage.cs`

实现 `IDetailPage`，绘制固定设计尺寸为 360×168 的详情页，并提供样式、粗细、配色和灵敏度切换按钮。设置变化写入宿主设置接口。

### `VisualizerSettingsPage.cs`

实现 `ISettingsPage` 和 `ICustomSettingsPage`。设置中心控件与详情页共用同一组设置键；下方自绘预览读取 `VisualizerState` 中的平滑值。此实现不依赖设置中心单独的 DLL。

### `StereoCapture.cs`

双声道模式的额外 WASAPI Loopback 捕获器。左右声道分别进行 Goertzel 频段分析，通过前后缓冲交换向绘制逻辑提供数据。仅双声道模式需要捕获；启动失败或数据中断时会重试，捕获不可用时绘制逻辑使用宿主单声道数据镜像回退。

### `PluginFont.cs`

从宿主 `FontConfig` 取得字体。宿主字体属于共享资源：每次绘制按需取得，不缓存，也不要对其调用 `Dispose`。

## 音频与绘制数据流

1. 普通柱形模式读取宿主每帧提供的 `WidgetFrame.Bars`，数据为 5 段归一化频谱。
2. 双声道模式额外从默认输出设备进行 WASAPI Loopback，左右声道分别分析；宿主的 `Bars` 不提供可供本插件使用的独立右声道数据。
3. `AudioVisualizerPlugin` 的周期回调根据 `State.Style` 启动、停止或恢复捕获。不要在 `Draw`、`HitTest` 或鼠标回调中打开音频设备或初始化 COM。
4. 组件和详情页绘制时调用 `VisualizerState.Feed` 更新平滑值。捕获缓冲只在当帧读取，不要把宿主传来的帧数组保存为长期状态。
5. 捕获路径使用双缓冲交接数据。修改线程模型或数组所有权时，特别检查音频回调与 UI 绘制的并发读写。

## 设置键和值

设置由宿主 `GetSetting` / `SetSetting` 保存。选择控件的值使用十进制索引字符串；宿主会按插件 ID 隔离设置键。

| 键 | 索引含义 | 当前缺省值 |
| --- | --- | --- |
| `style` | `0` 柱形；`1` 双声道 | `0` |
| `thickness` | `0` 细；`1` 标准；`2` 粗 | `1` |
| `color` | `0` 主题色；`1` 彩虹色；`2` 霓虹色 | `1` |
| `gainIdx` | `0` ×0.5；`1` ×0.8；`2` ×1；`3` ×1.5；`4` ×2 | `2` |

旧版本将灵敏度倍率写在 `gain` 键中。`ReadGainIndex` 会在新键不存在或无效时，将旧浮点值映射到最接近的当前倍率并写入 `gainIdx`。增加或重排选项时，保持已有索引含义，或明确设计迁移，避免用户设置错位。

设置中心改动会触发 `SettingsChanged`；插件需要重新读取设置，而不是只把事件当作刷新信号。若重读设置发现柱宽改变，也要使宿主重新测量组件布局。

## 渲染资源和线程约定

- `SKPaint`、`SKMaskFilter` 等 Skia 绘制资源在绘制方法内创建并及时释放；不要跨帧缓存这些对象。
- 不要释放宿主借用的 `SKTypeface`。
- 保持详情页固定设计坐标系的布局方式，避免展开动画尺寸变化导致控件跳动。
- 频谱捕获与 UI 绘制分属不同线程。避免在绘制线程等待锁、执行阻塞 I/O 或启动/停止 WASAPI。
- 任何修改布局尺寸缓存的设置，都要确认宿主会收到重新测量通知。

## 项目引用与依赖

`NPS.AudioVisualizer.csproj` 通过相对路径引用 `..\NotchPeninsula\NotchPeninsula.csproj`，并设置 `Private="false"`。SkiaSharp 版本为 2.88.8，NAudio.Core 和 NAudio.Wasapi 版本为 2.2.1，项目配置将它们作为编译引用，运行时依赖由宿主提供。

如果远程环境需要构建，两个仓库必须按相对路径并列放置，不能把插件源码放进宿主仓库目录（宿主项目的默认源文件通配可能把插件代码一并编译进宿主）：

```text
工作目录/
├── NotchPeninsula/       # 与目标版本匹配的宿主源码
└── NPS.AudioVisualizer/  # 本插件仓库
```

在托管 Windows runner 或所选远程开发平台中配置 .NET 10 SDK 和上述目录后，进入 `NPS.AudioVisualizer` 项目目录再构建。项目的 `InstallToHostPlugins` 目标会把 DLL 复制到宿主输出目录的 `plugins` 文件夹。不要在用户本机运行下面的远程构建命令：

```powershell
dotnet build NPS.AudioVisualizer.csproj -c Release
```

构建产物预期位于插件项目的 `bin\Release\net10.0-windows10.0.19041.0\NPS.AudioVisualizer.dll`。生成产物后，先核对它与目标宿主 API/运行时版本匹配，再通过远程平台提供的方式交付。
