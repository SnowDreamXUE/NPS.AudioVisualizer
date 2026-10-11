using NotchPeninsula.Plugins;
using SkiaSharp;

namespace NPS.AudioVisualizer;

/// <summary>
/// 插件入口：音频可视化（系统输出频谱）。
/// 单声道数据来源是宿主每帧下发的 WidgetFrame.Bars（WASAPI Loopback → 5 频段 0~1）；
/// 「双声道」样式另由 StereoCapture 自开一路 Loopback 拿真·左右声道（宿主 Bars 只含左声道混音）。
/// 设置同时注入「插件设置中心」（RegisterSettingsPage）与自带详情页，两处读写同一份注册表键值。
/// </summary>
public sealed class AudioVisualizerPlugin : INotchPlugin, IDisposable
{
    private IPluginHost? _host;
    private IDisposable? _stereoTimer;
    internal readonly VisualizerState State = new();
    internal readonly StereoCapture Stereo = new();

    // 反域名风格唯一 Id；DisplayName / Version / Author 显示在「我的插件」列表里
    public string Id => "com.nps.audiovisualizer";
    public string DisplayName => "音频可视化";
    public string Version => "1.3.0";
    public string Author => "CodeBuddy";

    public void Initialize(IPluginHost host)
    {
        _host = host;
        ReadSettings(host);
        host.SettingsChanged += OnSettingsChanged;

        // 注册组件（组件内部自带详情页，右键展开）
        host.RegisterWidget(new VisualizerWidget(this));

        // 把设置注入「插件设置中心」（NpsSettingsHub）：声明式控件 + 自绘预览，只注入一次
        host.RegisterSettingsPage(new VisualizerSettingsPage(this));

        // 立体声捕获按需启停：仅「双声道」样式下运行，后台线程周期检查（含断流自愈）
        _stereoTimer = host.ScheduleRefresh(TimeSpan.FromSeconds(1), TickStereoCapture);

        host.PostReminder(new ReminderData
        {
            Title = "音频可视化已加载",
            Body = "右键岛上的频谱组件，可调整样式 / 粗细 / 配色 / 灵敏度",
        });
        try { NotchPeninsula.Logger.Info("[AudioVisualizer] loaded v1.3.0"); } catch { }
    }

    /// <summary>双声道捕获的按需启停：样式切到双声道才开捕获，切走即停。</summary>
    private void TickStereoCapture()
    {
        bool want = State.Style == VisualizerState.StyleDual;
        if (want && !Stereo.IsRunning) Stereo.Start();
        else if (!want && Stereo.IsRunning) Stereo.Stop();
        else if (want) Stereo.RestartIfDead();
    }

    private void OnSettingsChanged()
    {
        if (_host is { } h) ReadSettings(h);
    }

    /// <summary>从注册表读回持久化设置（键由宿主自动加 Plugin.&lt;Id&gt;. 前缀隔离）。</summary>
    internal void ReadSettings(IPluginHost host)
    {
        State.Style = ParseIndex(host.GetSetting("style", "0"), VisualizerState.StyleCount);
        State.ColorMode = ParseIndex(host.GetSetting("color", "1"), 3);
        State.GainIndex = ReadGainIndex(host);

        int thick = ParseIndex(host.GetSetting("thickness", "1"), 3);
        if (thick != State.Thickness)
        {
            State.Thickness = thick;
            // 设置中心等外部途径改了粗细：岛上组件宽度随档位变化，必须通知宿主重测
            try { host.InvalidateWidgetLayout(); } catch { }
        }
    }

    /// <summary>
    /// 读灵敏度档位（索引）。≤1.2.0 版本把浮点倍率存在旧键 "gain" 里（"1"/"1.5"/"3"...），
    /// 这里做一次性迁移到新索引键 "gainIdx"（设置中心的选择框统一写索引）。
    /// </summary>
    private static int ReadGainIndex(IPluginHost host)
    {
        string raw = host.GetSetting("gainIdx", "");
        if (int.TryParse(raw, out int i) && i >= 0 && i < VisualizerState.GainSteps.Length) return i;

        float.TryParse(host.GetSetting("gain", "1"), out float v);
        int best = 2; float bestDist = float.MaxValue;
        for (int j = 0; j < VisualizerState.GainSteps.Length; j++)
        {
            float d = MathF.Abs(VisualizerState.GainSteps[j] - v);
            if (d < bestDist) { bestDist = d; best = j; }
        }
        try { host.SetSetting("gainIdx", best.ToString()); } catch { } // 迁移落盘（幂等，不会循环）
        return best;
    }

    private static int ParseIndex(string s, int mod)
        => int.TryParse(s, out int v) ? ((v % mod) + mod) % mod : 0;

    public void Dispose()
    {
        if (_host is { } h) h.SettingsChanged -= OnSettingsChanged;
        _stereoTimer?.Dispose();
        Stereo.Dispose();
    }
}

/// <summary>
/// 共享状态：设置项 + 平滑后的频段值（单声道 + 左右声道）。
/// 平滑数组只在渲染线程的 Draw 链路里读写；Feed 用时间戳去重，
/// 避免组件与详情页同帧各画一次时平滑被推进两遍。
/// </summary>
internal sealed class VisualizerState
{
    public const int Bands = 5;

    // 样式：0 柱形 / 1 双声道
    public const int StyleCount = 2;
    public const int StyleDual = 1;

    /// <summary>灵敏度档位（循环顺序即数组顺序；设置值统一存索引）。</summary>
    public static readonly float[] GainSteps = { 0.5f, 0.8f, 1f, 1.5f, 2f };
    public static readonly string[] GainNames = { "x0.5", "x0.8", "x1", "x1.5", "x2" };

    // 设置项（写：点击回调 / 设置变更事件；读：渲染线程。volatile 保证跨线程可见性）
    public volatile int Style;
    public volatile int Thickness = 1;  // 0 细 / 1 标准 / 2 粗
    public volatile int ColorMode;      // 0 主题 / 1 彩虹 / 2 霓虹
    public volatile int GainIndex = 2;  // 默认 ×1

    /// <summary>当前灵敏度倍率（由档位索引查表）。</summary>
    public float Gain => GainSteps[GainIndex];

    public readonly float[] Smooth = new float[Bands];  // 单声道（宿主 Bars）
    public readonly float[] SmoothL = new float[Bands]; // 左声道
    public readonly float[] SmoothR = new float[Bands]; // 右声道
    public readonly float[] Peak = new float[Bands];
    private readonly int[] _peakHold = new int[Bands];
    private long _lastFeedTick;

    private const float Attack = 0.55f;   // 上升快：跟得住节拍
    private const float Release = 0.10f;  // 下落慢：尾巴自然
    private const int HoldFrames = 24;    // 峰值驻留约 0.4s（60FPS）
    private const float PeakFall = 0.012f;

    /// <summary>
    /// 吃进一帧新频谱。mono 是宿主每帧双缓冲交换的 float[5]，left/right 来自插件自捕获，
    /// 都只在 Draw 当帧读取有效；left/right 为 null（捕获未运行）时回退用 mono 镜像。
    /// </summary>
    public void Feed(float[]? mono, float[]? left, float[]? right)
    {
        long now = Environment.TickCount64;
        if (now - _lastFeedTick < 8) return; // 同帧去重
        _lastFeedTick = now;

        float gain = Gain;
        for (int i = 0; i < Bands; i++)
        {
            float m = mono is not null && i < mono.Length ? mono[i] : 0f;
            float l = left is not null && i < left.Length ? left[i] : m;
            float r = right is not null && i < right.Length ? right[i] : m;

            Smooth[i] = Step(Smooth[i], m * gain);
            SmoothL[i] = Step(SmoothL[i], l * gain);
            SmoothR[i] = Step(SmoothR[i], r * gain);

            // 峰值驻留（柱形样式用，基于单声道）
            float s = Smooth[i];
            if (s >= Peak[i]) { Peak[i] = s; _peakHold[i] = HoldFrames; }
            else if (_peakHold[i] > 0) _peakHold[i]--;
            else Peak[i] = Math.Max(0f, Peak[i] - PeakFall);
        }
    }

    private static float Step(float s, float target)
    {
        target = Math.Clamp(target, 0f, 1f);
        return target > s ? s + (target - s) * Attack : s + (target - s) * Release;
    }

    /// <summary>当前是否有有效音频（用于「监听中 / 无音频」指示与双声道中线的亮灭）。</summary>
    public bool IsLive
    {
        get
        {
            for (int i = 0; i < Bands; i++)
                if (Smooth[i] > 0.02f || SmoothL[i] > 0.02f || SmoothR[i] > 0.02f) return true;
            return false;
        }
    }
}

/// <summary>柱形粗细档位：返回（柱宽, 柱间距），组件 / 详情页各一套尺寸。</summary>
internal static class BarMetrics
{
    public static (float bar, float gap) For(int thickness, bool detail)
        => thickness switch
        {
            0 => detail ? (22f, 24f) : (3f, 3.5f),
            2 => detail ? (48f, 20f) : (5.5f, 2.5f),
            _ => detail ? (36f, 24f) : (4f, 3f),
        };

    public static string Name(int t) => t switch { 0 => "细", 2 => "粗", _ => "标准" };
}

/// <summary>频段配色与柱形绘制，组件 / 详情页 / 设置中心预览共用。</summary>
internal static class SpectrumPaint
{
    private static readonly SKColor[] Rainbow =
    {
        SKColor.Parse("#3B82F6"), SKColor.Parse("#06B6D4"),
        SKColor.Parse("#22C55E"), SKColor.Parse("#EAB308"),
        SKColor.Parse("#EF4444"),
    };

    // 霓虹：高饱和荧光色，绘制时叠一层同色辉光
    private static readonly SKColor[] Neon =
    {
        SKColor.Parse("#FF2D95"), SKColor.Parse("#B026FF"), SKColor.Parse("#00E5FF"),
        SKColor.Parse("#39FF14"), SKColor.Parse("#FFE600"),
    };

    public static SKColor ColorFor(VisualizerState st, int band, WidgetFrame frame)
        => ColorFor(st, band, frame.Theme.TextColor);

    public static SKColor ColorFor(VisualizerState st, int band, SKColor themeText)
        => st.ColorMode switch
        {
            1 => Rainbow[band % Rainbow.Length],
            2 => Neon[band % Neon.Length],
            _ => themeText,
        };

    /// <summary>霓虹配色下带辉光。</summary>
    public static bool UseGlow(VisualizerState st) => st.ColorMode == 2;

    /// <summary>画一根圆角柱；glow=true 时先叠一层同色外发光再画实心柱（霓虹配色用）。</summary>
    public static void DrawGlowBar(SKCanvas canvas, SKRect r, float radius, SKColor color, byte alpha, bool glow)
    {
        if (glow)
        {
            using var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Solid, 4f);
            using var g = new SKPaint { IsAntialias = true, Color = color.WithAlpha((byte)(alpha * 0.45f)), MaskFilter = mf };
            canvas.DrawRoundRect(new SKRoundRect(r, radius), g);
        }
        using var p = new SKPaint { IsAntialias = true, Color = color.WithAlpha(alpha) };
        canvas.DrawRoundRect(new SKRoundRect(r, radius), p);
    }

    public static string StyleName(int s) => s == VisualizerState.StyleDual ? "双声道" : "柱形";
    public static string ColorName(int c) => c switch { 1 => "彩虹", 2 => "霓虹", _ => "主题" };
}
