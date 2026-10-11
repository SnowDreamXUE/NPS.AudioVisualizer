using NotchPeninsula.Plugins;
using SkiaSharp;

namespace NPS.AudioVisualizer;

/// <summary>
/// 详情页：大号频谱 + 样式设置面板（右键岛上组件展开）。
/// 排版永远按固定 360×168 设计坐标系计算，绘制时整体缩放对齐到当前 rect ——
/// 展开 / 收起的弹簧动画期间 rect 是插值尺寸，拿它排版会每帧跳位。
/// </summary>
internal sealed class VisualizerDetailPage : IDetailPage
{
    private const float LW = 360f, LH = 168f;

    // 频谱区（设计坐标系）
    private const float SpecTop = 38f, SpecBottom = 118f;
    private static readonly string[] BandNames = { "低频", "中低", "中频", "中高", "高频" };

    // 交互热区（设计坐标系；动画结束后 scale=1，与命中口径 1:1）
    // 四枚药丸：样式 / 粗细 / 配色 / 灵敏（宽 76、间距 8、左右边距 16）
    private static readonly SKRect PillStyle = new(16, 138, 92, 160);
    private static readonly SKRect PillThick = new(100, 138, 176, 160);
    private static readonly SKRect PillColor = new(184, 138, 260, 160);
    private static readonly SKRect PillGain = new(268, 138, 344, 160);
    private static readonly SKRect CloseBtn = new(330, 8, 352, 30);

    private readonly AudioVisualizerPlugin _plugin;
    private IPluginHost? _host;

    public VisualizerDetailPage(AudioVisualizerPlugin plugin) => _plugin = plugin;
    internal void Attach(IPluginHost host) => _host = host;

    // 宿主会把宽裁剪到 180~1000、高裁剪到 48~480，360×168 在安全区内
    public float MeasureWidth() => LW;
    public float MeasureHeight() => LH;

    public void Draw(SKCanvas canvas, SKRect rect, WidgetFrame frame)
    {
        if (frame.Alpha == 0) return;

        var st = _plugin.State;
        st.Feed(frame.Bars, _plugin.Stereo.FrontL, _plugin.Stereo.FrontR);

        // 固定坐标系排版 + 整体缩放（弹簧有过冲，封顶 1 免得边缘被裁）
        canvas.Save();
        float scale = Math.Min(rect.Height / LH, 1f);
        canvas.Translate(rect.MidX, rect.MidY);
        canvas.Scale(scale);
        canvas.Translate(-LW * 0.5f, -LH * 0.5f);

        byte a = frame.Alpha;
        var textColor = frame.Theme.TextColor.WithAlpha(a);
        var subColor = frame.Theme.SubTextColor.WithAlpha(a);

        // ---- 标题行 ----
        using (var title = new SKPaint { IsAntialias = true, TextSize = 13f, Typeface = PluginFont.ResolveBold(), Color = textColor })
            canvas.DrawText("音频频谱", 20, 25, title);
        using (var status = new SKPaint
        {
            IsAntialias = true,
            TextSize = 10.5f,
            Typeface = PluginFont.Resolve(),
            Color = st.IsLive ? SKColor.Parse("#22C55E").WithAlpha(a) : subColor,
        })
            canvas.DrawText(st.IsLive ? "● 监听中" : "○ 无音频", 84, 25, status);
        using (var close = new SKPaint { IsAntialias = true, TextSize = 14f, Typeface = PluginFont.Resolve(), Color = subColor, TextAlign = SKTextAlign.Center })
            canvas.DrawText("×", CloseBtn.MidX, CloseBtn.MidY + 5f, close);

        // ---- 频谱区（柱宽随粗细档位）----
        var (barW, gap) = BarMetrics.For(st.Thickness, detail: true);
        float span = VisualizerState.Bands * barW + (VisualizerState.Bands - 1) * gap;
        float x0 = (LW - span) * 0.5f;
        bool glow = SpectrumPaint.UseGlow(st);

        if (st.Style == VisualizerState.StyleDual)
        {
            DrawDualSpectrum(canvas, x0, span, barW, gap, frame, a, subColor, glow);
        }
        else
        {
            float radius = Math.Min(6f, barW * 0.18f);
            for (int i = 0; i < VisualizerState.Bands; i++)
            {
                float x = x0 + i * (barW + gap);
                DrawBar(canvas, x, st.Smooth[i], st.Peak[i], barW, radius,
                    SpectrumPaint.ColorFor(st, i, frame), frame, a, glow);
            }
        }

        // 频段名（两种样式共用）
        using var label = new SKPaint { IsAntialias = true, TextSize = 10f, Typeface = PluginFont.Resolve(), Color = subColor, TextAlign = SKTextAlign.Center };
        for (int i = 0; i < VisualizerState.Bands; i++)
            canvas.DrawText(BandNames[i], x0 + i * (barW + gap) + barW * 0.5f, SpecBottom + 14, label);

        // ---- 设置按钮（点击循环切换，写入注册表持久化）----
        DrawPill(canvas, PillStyle, $"样式:{SpectrumPaint.StyleName(st.Style)}", frame);
        DrawPill(canvas, PillThick, $"粗细:{BarMetrics.Name(st.Thickness)}", frame);
        DrawPill(canvas, PillColor, $"配色:{SpectrumPaint.ColorName(st.ColorMode)}", frame);
        DrawPill(canvas, PillGain, $"灵敏:{VisualizerState.GainNames[st.GainIndex]}", frame);

        canvas.Restore();
    }

    /// <summary>柱形：圆角柱 + 峰值驻留帽。</summary>
    private static void DrawBar(SKCanvas canvas, float x, float v, float peak, float barW, float radius,
        SKColor color, WidgetFrame frame, byte alpha, bool glow)
    {
        float specH = SpecBottom - SpecTop;
        float h = Math.Max(3f, v * specH);
        SpectrumPaint.DrawGlowBar(canvas, new SKRect(x, SpecBottom - h, x + barW, SpecBottom), radius, color, alpha, glow);

        float ph = Math.Max(h, peak * specH);
        if (ph > 8f)
        {
            using var cap = new SKPaint { IsAntialias = true, Color = frame.Theme.TextColor.WithAlpha((byte)(alpha * 0.55f)) };
            canvas.DrawRect(x, SpecBottom - ph - 3f, barW, 2.2f, cap);
        }
    }

    /// <summary>双声道：上半左耳（L）、下半右耳（R），绕中线镜像；无声音时居中一条线。</summary>
    private void DrawDualSpectrum(SKCanvas canvas, float x0, float span, float barW, float gap,
        WidgetFrame frame, byte a, SKColor subColor, bool glow)
    {
        var st = _plugin.State;
        float cy = (SpecTop + SpecBottom) * 0.5f;
        float halfH = (SpecBottom - SpecTop) * 0.5f - 6f;
        float lineLeft = Math.Max(8f, x0 - 20);
        float lineRight = Math.Min(LW - 8f, x0 + span + 20);

        // L / R 声道标记
        using (var tag = new SKPaint { IsAntialias = true, TextSize = 10f, Typeface = PluginFont.ResolveBold(), Color = subColor, TextAlign = SKTextAlign.Center })
        {
            canvas.DrawText("L", x0 - 14 > 10 ? x0 - 14 : 14, SpecTop + 11, tag);
            canvas.DrawText("R", x0 - 14 > 10 ? x0 - 14 : 14, SpecBottom, tag);
        }

        if (!st.IsLive)
        {
            // 无声音：居中一条线（主题文字色 —— 深色主题下即白线，浅色主题下保证可见）
            using var line = new SKPaint { IsAntialias = true, Color = frame.Theme.TextColor.WithAlpha((byte)(a * 0.9f)) };
            canvas.DrawRect(lineLeft, cy - 1f, lineRight - lineLeft, 2f, line);
            return;
        }

        // 有声音：弱化中线 + 上左下右两组镜像柱
        using (var sep = new SKPaint { IsAntialias = true, Color = frame.Theme.TextColor.WithAlpha((byte)(a * 0.25f)) })
            canvas.DrawRect(lineLeft, cy - 0.5f, lineRight - lineLeft, 1f, sep);

        float radius = Math.Min(5f, barW * 0.16f);
        for (int i = 0; i < VisualizerState.Bands; i++)
        {
            float x = x0 + i * (barW + gap);
            float vl = st.SmoothL[i], vr = st.SmoothR[i];

            SpectrumPaint.DrawGlowBar(canvas,
                new SKRect(x, cy - 4f - Math.Max(3f, vl * halfH), x + barW, cy - 4f),
                radius, SpectrumPaint.ColorFor(st, i, frame), a, glow);
            SpectrumPaint.DrawGlowBar(canvas,
                new SKRect(x, cy + 4f, x + barW, cy + 4f + Math.Max(3f, vr * halfH)),
                radius, SpectrumPaint.ColorFor(st, i, frame), a, glow);
        }
    }

    /// <summary>药丸形设置按钮。</summary>
    private static void DrawPill(SKCanvas canvas, SKRect r, string text, WidgetFrame frame)
    {
        byte a = frame.Alpha;
        using (var bg = new SKPaint { IsAntialias = true, Color = frame.Theme.TextColor.WithAlpha((byte)(a * 0.10f)) })
            canvas.DrawRoundRect(new SKRoundRect(r, 9f), bg);
        using var tp = new SKPaint
        {
            IsAntialias = true,
            TextSize = 10f,
            Typeface = PluginFont.Resolve(),
            Color = frame.Theme.TextColor.WithAlpha((byte)(a * 0.85f)),
            TextAlign = SKTextAlign.Center,
        };
        canvas.DrawText(text, r.MidX, r.MidY + 3.6f, tp);
    }

    public WidgetHit HitTest(float x, float y, SKRect rect)
    {
        if (CloseBtn.Contains(x, y)) return new WidgetHit("close");
        if (PillStyle.Contains(x, y)) return new WidgetHit("style");
        if (PillThick.Contains(x, y)) return new WidgetHit("thickness");
        if (PillColor.Contains(x, y)) return new WidgetHit("color");
        if (PillGain.Contains(x, y)) return new WidgetHit("gain");
        return WidgetHit.None;
    }

    public void OnAction(string? action, float x, float y)
    {
        if (_host is not { } h) return;
        var st = _plugin.State;
        switch (action)
        {
            case "close":
                h.CloseDetailPage();
                break;
            case "style":
                st.Style = (st.Style + 1) % VisualizerState.StyleCount;
                h.SetSetting("style", st.Style.ToString());
                break;
            case "thickness":
                st.Thickness = (st.Thickness + 1) % 3;
                h.SetSetting("thickness", st.Thickness.ToString());
                // 岛组件宽度随粗细变化：宿主缓存了 MeasureWidth，必须通知重测
                h.InvalidateWidgetLayout();
                break;
            case "color":
                st.ColorMode = (st.ColorMode + 1) % 3;
                h.SetSetting("color", st.ColorMode.ToString());
                break;
            case "gain":
                // 与设置中心的选择框同口径：统一存索引（"gainIdx"）
                st.GainIndex = (st.GainIndex + 1) % VisualizerState.GainSteps.Length;
                h.SetSetting("gainIdx", st.GainIndex.ToString());
                break;
        }
    }
}
