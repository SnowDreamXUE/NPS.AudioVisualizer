using NotchPeninsula.Plugins;
using SkiaSharp;

namespace NPS.AudioVisualizer;

/// <summary>
/// 灵动岛上的迷你频谱组件：柱形 / 双声道 两种样式，柱宽随「粗细」设置变化。
/// 宽度随粗细档位变化 —— 粗细切换后必须调 IPluginHost.InvalidateWidgetLayout
/// 通知宿主重测（宿主按注册表版本缓存宽度，不会每帧问）。
/// 左键单击与右键默认行为一致：开合详情页。
/// </summary>
internal sealed class VisualizerWidget : IWidget
{
    private const float PadX = 4f;

    private readonly AudioVisualizerPlugin _plugin;
    private readonly VisualizerDetailPage _detail;
    private IPluginHost? _host;

    public VisualizerWidget(AudioVisualizerPlugin plugin)
    {
        _plugin = plugin;
        _detail = new VisualizerDetailPage(plugin); // 详情页建一次缓存，别每次访问都 new
    }

    public string Id => "com.nps.audiovisualizer.widget";
    public string DisplayName => "音频频谱";
    public IDetailPage? DetailPage => _detail;

    /// <summary>宽度 = 内边距×2 + 5×柱宽 + 4×间距，随粗细档位变化；自动隐藏生效时宽度归零，宿主重排即收回空位。</summary>
    public float MeasureWidth(float availableHeight)
    {
        if (_plugin.WidgetHidden) return 0f;

        var (bar, gap) = BarMetrics.For(_plugin.State.Thickness, detail: false);
        return PadX * 2 + VisualizerState.Bands * bar + (VisualizerState.Bands - 1) * gap;
    }

    public void Draw(SKCanvas canvas, SKRect rect, WidgetFrame frame)
    {
        if (frame.Alpha == 0) return;

        var st = _plugin.State;
        st.Feed(frame.Bars, _plugin.Stereo.FrontL, _plugin.Stereo.FrontR); // 只当帧读取

        float bottom = rect.Bottom - 3f;
        float maxH = Math.Max(6f, rect.Height - 6f);
        var (barW, gap) = BarMetrics.For(st.Thickness, detail: false);
        bool glow = SpectrumPaint.UseGlow(st);

        // 双声道：上左下右，无声音时居中一条线
        if (st.Style == VisualizerState.StyleDual)
        {
            DrawDual(canvas, rect, frame, st, maxH, barW, gap, glow);
            return;
        }

        // 柱形
        if (!st.IsLive)
        {
            using var line = new SKPaint
            {
                IsAntialias = true,
                Color = frame.Theme.SubTextColor.WithAlpha((byte)(frame.Alpha * 0.55f)),
            };
            canvas.DrawRect(rect.Left + PadX, rect.Bottom - 1f, rect.Width - PadX * 2f, 1f, line);
            return;
        }

        float radius = Math.Min(1.8f, barW * 0.4f);
        for (int i = 0; i < VisualizerState.Bands; i++)
        {
            float v = st.Smooth[i];
            float h = v * maxH;
            float x = rect.Left + PadX + i * (barW + gap);
            SpectrumPaint.DrawGlowBar(canvas, new SKRect(x, bottom - h, x + barW, bottom),
                radius, SpectrumPaint.ColorFor(st, i, frame), frame.Alpha, glow);
        }
    }

    /// <summary>双声道：上半左耳、下半右耳；无声音时居中一条线（主题文字色，深色主题下即白线）。</summary>
    private static void DrawDual(SKCanvas canvas, SKRect rect, WidgetFrame frame, VisualizerState st,
        float maxH, float barW, float gap, bool glow)
    {
        float cy = rect.MidY;
        float half = Math.Max(3f, maxH * 0.5f - 1.5f);

        if (!st.IsLive)
        {
            using var line = new SKPaint { IsAntialias = true, Color = frame.Theme.TextColor.WithAlpha((byte)(frame.Alpha * 0.9f)) };
            canvas.DrawRect(rect.Left + 3f, cy - 0.8f, rect.Width - 6f, 1.6f, line);
            return;
        }

        // 有声音：弱化中线 + 上左下右两组镜像柱
        using (var sep = new SKPaint { IsAntialias = true, Color = frame.Theme.TextColor.WithAlpha((byte)(frame.Alpha * 0.25f)) })
            canvas.DrawRect(rect.Left + 3f, cy - 0.5f, rect.Width - 6f, 1f, sep);

        float radius = Math.Min(1.6f, barW * 0.4f);
        for (int i = 0; i < VisualizerState.Bands; i++)
        {
            float x = rect.Left + PadX + i * (barW + gap);
            float vl = st.SmoothL[i], vr = st.SmoothR[i];

            SpectrumPaint.DrawGlowBar(canvas,
                new SKRect(x, cy - 2f - Math.Max(1f, vl * half), x + barW, cy - 2f),
                radius, SpectrumPaint.ColorFor(st, i, frame), frame.Alpha, glow);
            SpectrumPaint.DrawGlowBar(canvas,
                new SKRect(x, cy + 2f, x + barW, cy + 2f + Math.Max(1f, vr * half)),
                radius, SpectrumPaint.ColorFor(st, i, frame), frame.Alpha, glow);
        }
    }

    public WidgetHit HitTest(float x, float y, SKRect rect)
        => x >= 0 && x <= rect.Width && y >= 0 && y <= rect.Height
            ? new WidgetHit("toggle")
            : WidgetHit.None;

    public void OnLeftClick(string? action, float x, float y)
        => _host?.ToggleDetailPage(Id); // 左键与右键行为一致：开合详情页

    public void OnRightClick() { } // 右键维持宿主默认行为（展开详情页）

    public void OnActivate(IPluginHost host)
    {
        _host = host;
        _plugin.ReadSettings(host); // 读回上次保存的样式 / 粗细 / 配色 / 灵敏度
        _detail.Attach(host);
    }

    public void OnDeactivate() { }
}
