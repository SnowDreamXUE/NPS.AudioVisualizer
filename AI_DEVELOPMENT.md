# AI 开发交接说明

本文档供接手 `NPS.AudioVisualizer` 的 AI 开发工具阅读。它记录当前代码结构、宿主耦合点，以及不在用户本机编译的开发方式。

## Git 工作流与提交规范

所有新功能、Bug 修复和文档变更都遵循下面的流程，不要直接在 `main` 分支上修改。

### 开始任务前

1. 先检查工作区和当前分支：`git status --short --branch`。不要覆盖、丢弃或重置用户已有的未提交改动；如果这些改动会影响本任务，应先说明并等待处理方式。
2. 更新基础分支：切换到 `main` 后执行 `git pull --ff-only origin main`。禁止用强制重置覆盖远端或本地改动；如果同步发生冲突，先停止并处理冲突。
3. 从最新的 `main` 创建任务分支，再开始编辑：
   - 新功能：`<prefix>/feat-简短名称`
   - Bug 修复：`<prefix>/fix-简短名称`
   - 文档或维护：`<prefix>/docs-简短名称` 或 `<prefix>/chore-简短名称`
   - `<prefix>` 由团队或所使用的开发工具约定，例如团队缩写、用户名或工具名；不要把某一种工具名称写死为项目唯一前缀。
4. 分支名使用简短、稳定、可读的描述。后续操作都在任务分支上完成，不要直接提交到 `main`。

### 修改过程中

- 先阅读相关源码、宿主 API 文档和现有约定，再确定方案；保持改动范围与任务一致，不顺手重构无关代码。
- AI 设计方案、分析记录、临时补丁、截图和调试输出统一放在仓库根目录的 `.ai-work/` 下。这些文件是本地缓存，不属于交付内容，不要 `git add` 或提交。
- 只有明确属于项目交付物的源码、文档、资源和经过确认的发布产物才加入暂存区。提交前检查未跟踪文件，避免把密钥、个人配置、IDE 文件、构建输出或缓存带入提交。
- 不要擅自修改或删除用户已有改动；不确定文件是否应纳入任务时，先保留并询问。

### 完成任务前

在提交前完成以下检查：

1. 查看 `git diff`、`git diff --stat` 和 `git status --short`，确认差异只包含本任务内容。
2. 执行 `git diff --check`，修复空白错误；按项目约束进行源码审阅和可用的远程构建/测试。不要在用户本机执行 `dotnet build`、`dotnet test` 或其他本地编译流程。
3. 再次确认 `.ai-work/`、截图、日志、构建目录、个人配置和敏感信息没有进入暂存区。不要使用无法逐项确认内容的 `git add -A`。
4. 功能或修复完成且检查通过后再提交。一个提交应表达一个完整目的，避免把未完成方案或无关格式化混入提交。

### 提交信息与后续操作

- 提交信息使用清晰的约定式格式：`<type>(<scope>): <简短摘要>`，例如 `fix(audio): 修复默认设备切换后频谱冻结`、`docs(workflow): 补充 AI 开发分支规范`。
- `type` 优先使用 `feat`、`fix`、`docs`、`refactor`、`test`、`chore`；摘要说明实际结果，避免使用“更新代码”“一些修改”等无信息描述。必要时在提交正文说明原因、行为变化和验证方式。
- 提交后记录分支名、提交 ID、验证结果和未提交文件状态。除非用户明确要求，不要自动推送、合并、删除分支或改写已有提交。

## 开发约束

- 用户希望在其他平台继续开发，**不要在用户本机执行 `dotnet build`、`dotnet test` 或其他本地编译流程**。
- 代码改动应以源码审阅为主。若任务需要编译产物，只能使用用户选定的远程开发平台或托管构建环境；说明构建是否成功以及产物位置。
- 仓库当前没有 CI 构建工作流或测试工程；`releases/` 目录中的预编译 DLL 对应最近一次远程构建，不代表最新源码改动已通过构建或测试。
- 改动宿主接口前，先查阅宿主对应版本的官方 API 文档和源码。不要仅凭其他 NotchPeninsula 版本的接口猜测。
- 尽量保持插件仍以单个 `NPS.AudioVisualizer.dll` 分发；不要把宿主运行时已提供的依赖复制进插件目录。

## 项目与宿主

- 插件 ID：`com.nps.audiovisualizer`
- 当前插件版本：`1.4.0`
- 目标框架：`net10.0-windows10.0.19041.0`
- 插件入口：`AudioVisualizerPlugin`
- 宿主 API 命名空间：`NotchPeninsula.Plugins`
- 目标宿主版本：**NotchPeninsula 正式版 2.0.0**（GitHub tag `NPS-v2.0`，release「NotchPeninsula 2.0」）。当前仓库没有包含宿主源码，变更兼容性前应核对目标 tag 的实际接口。

官方参考：

- [NotchPeninsula 仓库](https://github.com/GEORGEWWWU/NotchPeninsula)
- [NPS-v2.0 tag 的 NPS_PluginsAPI.md 插件开发说明](https://github.com/GEORGEWWWU/NotchPeninsula/blob/NPS-v2.0/NPS_PluginsAPI.md)
- 若目标版本有对应 tag，应优先使用该 tag 的 API 文档和宿主源码，不要默认 `main` 与已发布版本完全一致。

## 当前实现结构

仓库布局如下。`.csproj` 保留在仓库根目录以维持 `..\NotchPeninsula` 相对引用；源码按职责放在 `src/` 子目录，但所有文件统一使用 `NPS.AudioVisualizer` 命名空间（不随文件夹拆分），跨文件引用无需 using：

```text
NPS.AudioVisualizer/
├── NPS.AudioVisualizer.csproj
├── src/
│   ├── AudioVisualizerPlugin.cs
│   ├── Core/VisualizerState.cs
│   ├── Rendering/{BarMetrics, SpectrumPaint, PluginFont}.cs
│   ├── Audio/StereoCapture.cs
│   └── UI/{VisualizerWidget, VisualizerDetailPage, VisualizerSettingsPage}.cs
└── releases/NPS.AudioVisualizer.dll   # 预编译分发产物
```

### `src/AudioVisualizerPlugin.cs`

实现 `INotchPlugin` 和 `IDisposable`。`Initialize` 读取持久化设置、订阅 `SettingsChanged`、注册岛组件及插件设置页，并通过 `ScheduleRefresh` 周期性启停立体声捕获。捕获需求为「双声道样式」或「自动隐藏开启」二者之一。`Dispose` 退订事件、释放计划任务和捕获资源。

自动隐藏状态机也在该周期回调中驱动：静音满 1500ms（`HideDelayMs`）后把 `WidgetHidden` 置真并触发 `InvalidateWidgetLayout`；有声音、关闭开关或重置时经 `EnsureVisible` 恢复。只有捕获监测在运行时才允许隐藏，否则隐藏后没有恢复信号。

### `src/Core/VisualizerState.cs`

保存 UI 共用状态、增益档位、平滑后的频段值和峰值，以及 `AutoHideWhenSilent` 开关。岛组件与详情页共用状态，`Feed` 会按时间去重，避免同一帧因两个绘制入口而重复推进平滑值。

### `src/Rendering/BarMetrics.cs`

柱形粗细档位的尺寸表：按档位返回（柱宽, 柱间距），组件与详情页各一套尺寸。

### `src/Rendering/SpectrumPaint.cs`

频段配色（主题 / 彩虹 / 霓虹）与圆角柱绘制（霓虹配色带辉光），组件、详情页与设置中心预览共用。

### `src/UI/VisualizerWidget.cs`

实现 `IWidget`，负责灵动岛上的迷你频谱。组件宽度由柱宽档位决定；自动隐藏生效时 `MeasureWidth` 返回 0（宿主宽度为 0 的组件不占插件行空间，且不再回调 `Draw`/`HitTest`，恢复依赖周期 tick 与独立声音监测）。粗细变化后需要调用 `IPluginHost.InvalidateWidgetLayout()`，让宿主重新测量宽度。左键操作打开或关闭详情页。

### `src/UI/VisualizerDetailPage.cs`

实现 `IDetailPage`，绘制固定设计尺寸为 360×192 的详情页。设置区布局：四项设置两两一行，名称在左、选项片在右（候选项全部直接列出，点击即选中，动作名 `key:index`）；「无音频时自动隐藏」独占一行且与普通设置同列宽，开关动作 `autoHide`；「重置全部」按钮固定在该行最右侧，动作 `reset`，恢复五项默认值（柱形 / 标准 / 彩虹色 / ×1 / 关闭自动隐藏）。选项片命中区在 `Draw` 每帧按当前字体度量重建，`HitTest` 复用最近一帧结果。设置变化写入宿主设置接口。

### `src/UI/VisualizerSettingsPage.cs`

实现 `ISettingsPage` 和 `ICustomSettingsPage`。声明式控件含四个 `ChoiceSetting` 与一个 `ToggleSetting("autoHide", "无音频时自动隐藏", false)`；设置中心控件与详情页共用同一组设置键，开关值统一小写 `true` / `false`（与设置中心 NpsSettingsHub 的写入口径一致，读取侧同时兼容 `True` / `1`）。下方自绘预览读取 `VisualizerState` 中的平滑值。此实现不依赖设置中心单独的 DLL。

### `src/Audio/StereoCapture.cs`

双声道模式的额外 WASAPI Loopback 捕获器，同时承担自动隐藏的声音活动监测。左右声道分别进行 Goertzel 频段分析，通过前后缓冲交换向绘制逻辑提供数据；`OnAudioData` 额外跟踪采样峰值，超过 `ActivityThreshold`（约 -56dBFS）即刷新 `_lastAudioTicks`，`IsAudioActive` 据此判定最近 700ms 内是否有真实声音（数字静音与系统静音恒为 0）。双声道样式或自动隐藏开启时运行；启动失败或数据中断时会重试，捕获不可用时绘制逻辑使用宿主单声道数据镜像回退，自动隐藏保持组件可见。

### `src/Rendering/PluginFont.cs`

从宿主 `FontConfig` 取得字体。宿主字体属于共享资源：每次绘制按需取得，不缓存，也不要对其调用 `Dispose`。

## 音频与绘制数据流

1. 普通柱形模式读取宿主每帧提供的 `WidgetFrame.Bars`，数据为 5 段归一化频谱。
2. 双声道模式额外从默认输出设备进行 WASAPI Loopback，左右声道分别分析；宿主的 `Bars` 不提供可供本插件使用的独立右声道数据。
3. `AudioVisualizerPlugin` 的周期回调根据 `State.Style == StyleDual || State.AutoHideWhenSilent` 启动、停止或恢复捕获，并顺带驱动自动隐藏状态机。不要在 `Draw`、`HitTest` 或鼠标回调中打开音频设备或初始化 COM。
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
| `autoHide` | 开关：小写 `true` / `false`（读取兼容 `True` / `1`） | `false` |

开关由 `ToggleSetting` 声明、设置中心与详情页两处写入；设置中心的 NpsSettingsHub 对开关同样使用小写 `true` / `false`。`ReadSettings` 读取成功后无需额外动作，隐藏/恢复由周期 tick 判定；关闭开关或重置会立即经 `EnsureVisible` 恢复组件。

旧版本将灵敏度倍率写在 `gain` 键中。`ReadGainIndex` 会在新键不存在或无效时，将旧浮点值映射到最接近的当前倍率并写入 `gainIdx`。增加或重排选项时，保持已有索引含义，或明确设计迁移，避免用户设置错位。

设置中心改动会触发 `SettingsChanged`；插件需要重新读取设置，而不是只把事件当作刷新信号。若重读设置发现柱宽改变，也要使宿主重新测量组件布局。

## 渲染资源和线程约定

- `SKPaint`、`SKMaskFilter` 等 Skia 绘制资源在绘制方法内创建并及时释放；不要跨帧缓存这些对象。
- 不要释放宿主借用的 `SKTypeface`。
- 保持详情页固定设计坐标系的布局方式，避免展开动画尺寸变化导致控件跳动。
- 频谱捕获与 UI 绘制分属不同线程。避免在绘制线程等待锁、执行阻塞 I/O 或启动/停止 WASAPI。
- 任何修改布局尺寸缓存的设置，都要确认宿主会收到重新测量通知。

## 项目引用与依赖

`NPS.AudioVisualizer.csproj` 通过相对路径引用 `..\NotchPeninsula\NotchPeninsula.csproj`，并设置 `Private="false"`。SkiaSharp 版本为 2.88.8，NAudio.Core 和 NAudio.Wasapi 版本为 2.2.1，项目配置将它们作为编译引用，运行时依赖由宿主提供。以上框架与包版本已核对与宿主 2.0.0（tag `NPS-v2.0`）一致：宿主 `NotchPeninsula.csproj` 同为 `net10.0-windows10.0.19041.0`，运行时加载相同版本的 SkiaSharp 与 NAudio 程序集，插件不随带这些依赖。

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

Linux/macOS 环境构建需追加 Windows RID 与目标框架开关（宿主引用 WindowsDesktop 框架，缺 `-r win-x64` 会报 NETSDK1082）：

```bash
dotnet build NPS.AudioVisualizer.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true
```

此时产物位于 `bin/Release/net10.0-windows10.0.19041.0/win-x64/NPS.AudioVisualizer.dll`。`releases/` 目录中的分发 DLL 应使用与当前源码一致的最新构建产物。
