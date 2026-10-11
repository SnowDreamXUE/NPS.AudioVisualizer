# NPS.AudioVisualizer — NotchPeninsula 音频可视化插件

给 [NotchPeninsula](https://github.com/GEORGEWWWU/NotchPeninsula)（Win10/11 刘海/灵动岛组件）开发的音频可视化插件。
在灵动岛上实时显示系统输出的 5 频段频谱，随音乐跳动。

- **插件 Id**：`com.nps.audiovisualizer`
- **适用宿主版本**：NPS v1.9.5（按该版本 tag `NPS-v1.9.5` 的插件 API 编译）
- **数据来源**：宿主每帧下发的 `WidgetFrame.Bars`（WASAPI Loopback 捕获系统输出 → 5 频段 0~1 归一化）；
  「双声道」样式另由插件自开一路 Loopback 获取真·左右声道（见下）。

## 功能

| 位置 | 内容 |
| --- | --- |
| 岛上组件（宽度随粗细档位 37~46px） | 迷你频谱，样式与详情页联动切换，左键单击开合详情页 |
| 详情页（右键展开，360×168） | 大号频谱 + 频段名 + 「监听中/无音频」状态指示 + 设置按钮 |
| **设置中心（NpsSettingsHub）** | 设置已注入官方「插件设置中心」：四个选择框 + 一块实时预览自绘区 |
| 样式 | 柱形（带峰值驻留帽）/ **双声道** |
| 柱形粗细 | 细 / 标准 / 粗 三档（组件与详情页同步变化） |
| 配色 | 主题色（跟随宿主深浅主题）/ 彩虹色 / **霓虹色**（高饱和荧光 + 同色辉光） |
| 灵敏度 | ×0.5 → ×0.8 → ×1 → ×1.5 → ×2 循环 |
| 持久化 | 全部设置写入注册表（`Plugin.com.nps.audiovisualizer.*`），重启后保留 |

## 设置中心接入说明（v1.3.0 起）

装了官方「插件设置中心」（NpsSettingsHub）后，本插件的设置会出现在它的统一界面里：

- **声明式控件**（`ISettingsPage`）：频谱样式 / 柱形粗细 / 配色 / 灵敏度 四个选择框；
- **自绘区**（`ICustomSettingsPage`）：一块跟随当前设置的**实时频谱预览**（数据取共享状态的平滑值）。
- **值域口径与设置中心一致**：选择框统一存**索引**的十进制串。灵敏度在 ≤1.2.0 版本存的是浮点倍率
  （旧键 `gain`），v1.3.0 起迁移为索引键 `gainIdx` —— 首次加载自动迁移，用户无感知。
- **双向同步**：设置中心改 → 触发本插件 `SettingsChanged` → 重读设置即时生效（含粗细变化时
  补调 `InvalidateWidgetLayout` 通知宿主重测岛组件宽度）；详情页按钮改 → 设置中心 2 秒轮询后同步显示。
- 不引用设置中心的 dll，只用宿主已有接口（`RegisterSettingsPage`），没装设置中心也能正常用。

## 双声道模式说明

- **上 = 左耳（L），下 = 右耳（R）**，绕中线镜像跳动；详情页左侧有 L / R 标记。
- **无声音时收成居中一条线**（用主题文字色绘制：深色主题下即白线；浅色主题下自动变深，避免白底看不见）。
- **数据是真实的左右声道**：宿主自带的 `WidgetFrame.Bars` 只取左声道混成单声道（见宿主 `Media/AudioAnalyzer.cs`），
  给不了右耳数据，所以插件按文档「数据由插件自行处理」的约定，用 NAudio 自开一路 WASAPI Loopback，
  按左/右声道分别跑与宿主相同的 Goertzel 频段分析。NAudio 由宿主进程提供（同版本 2.2.1），插件仍是单 dll。
- 捕获**按需启停**：仅切到「双声道」样式才运行（启动延迟 ≤1s），切走即停；断流自动重建。
  捕获失败（设备被独占等）时自动回退为镜像单声道显示，不会空白。

## 安装（免编译）

把 `NPS.AudioVisualizer.dll` 复制到主程序 exe 同级的 `plugins` 文件夹（如 `C:\NotchPeninsula\plugins\`），
重启程序，或在 **设置 → 我的插件** 里点导入并选中该 dll。
显示位置与开关在 **设置 → 显示设置 → 显示内容** 里调整。

## 从源码编译

1. 克隆宿主源码（插件需要引用主程序工程）：

   ```
   git clone https://github.com/GEORGEWWWU/NotchPeninsula.git
   ```

2. 把本插件目录 `NPS.AudioVisualizer` 放到与仓库目录**平级**的位置：

   ```
   工作目录/
   ├── NotchPeninsula/        ← 克隆的仓库（含 NotchPeninsula.csproj）
   └── NPS.AudioVisualizer/   ← 本插件（csproj 里用相对路径 ..\NotchPeninsula\NotchPeninsula.csproj 引用）
   ```

   ⚠️ 不要把插件目录放进仓库目录里面——宿主工程默认通配 `**\*.cs`，会把插件源码也编进宿主程序集
   （表现为 CS0436 类型冲突警告）。

3. 安装 .NET 10 SDK 后编译：

   ```
   cd NPS.AudioVisualizer
   dotnet build NPS.AudioVisualizer.csproj -c Release
   ```

   产物在 `bin\Release\net10.0-windows10.0.19041.0\` 下，且会被自动复制到
   `NotchPeninsula\bin\Release\net10.0-windows10.0.19041.0\plugins\`（csproj 里的 InstallToHostPlugins 目标）。

   在非 Windows 系统上编译需追加：`-r win-x64 -p:EnableWindowsTargeting=true`。

## 项目结构

```
NPS.AudioVisualizer/
├── NPS.AudioVisualizer.csproj   # net10.0-windows10.0.19041.0 类库，引用宿主工程（Private=false）
├── AudioVisualizerPlugin.cs     # 入口类（INotchPlugin）+ 共享状态 + 粗细档位 + 配色/辉光 + 设置迁移
├── VisualizerWidget.cs          # 岛上迷你频谱组件（IWidget，宽度随粗细变化）
├── VisualizerDetailPage.cs      # 详情页：大频谱 + 样式/粗细/配色/灵敏设置（IDetailPage）
├── VisualizerSettingsPage.cs    # 设置中心注入页（ISettingsPage + ICustomSettingsPage 自绘预览）
├── StereoCapture.cs             # 立体声捕获：自开一路 WASAPI Loopback，左右声道分别 Goertzel
├── PluginFont.cs                # 宿主字体借用（现取现用，绝不缓存/释放）
└── README.md
```

## 实现要点（对照 NPS_PluginsAPI.md 与设置中心接入文档）

- 频谱只从 `WidgetFrame.Bars` / 捕获前缓冲 **当帧读取**（双缓冲无锁交换），平滑值存插件自有字段；
- 粗细切换后调 `IPluginHost.InvalidateWidgetLayout()` 通知宿主重测组件宽度（含设置中心改动路径）；
- 画笔 `SKPaint` / 辉光 `SKMaskFilter` 一律在 `Draw` 内临时创建、`using` 释放，不跨线程缓存；
- 字体借 `FontConfig`，每次现取、绝不 `Dispose`（设置中心自绘区同）；
- 设置读写走 `GetSetting`/`SetSetting`，订阅 `SettingsChanged` 并重读全部设置（不是只当通知）；
- 设置页只在 `Initialize` 注入一次；控件 Key 不含 `|`；`Title`/`Controls`/`MeasureHeight` 全部 O(1)；
- 音频捕获在 `ScheduleRefresh` 后台线程启停，绝不在渲染线程做 COM 初始化；
- 详情页按固定 360×168 设计坐标系排版、绘制时整体缩放，弹簧动画期间不跳版。

## 更新记录

- **1.3.0**：设置接入官方「插件设置中心」（四个选择框 + 自绘实时预览）；灵敏度存储迁移为索引键
  `gainIdx`（旧浮点键 `gain` 自动迁移）；设置中心改粗细时自动通知宿主重测组件宽度。
- **1.2.0**：新增柱形粗细三档；移除圆点与曲线样式；灵敏度改为 ×0.5/×0.8/×1/×1.5/×2；
  配色改为 主题 / 彩虹 / 霓虹。
- **1.1.0**：修复「样式切换在岛上组件不生效」；新增双声道样式（上左下右真声道，无声音居中一条线）。
- **1.0.0**：首个版本。
