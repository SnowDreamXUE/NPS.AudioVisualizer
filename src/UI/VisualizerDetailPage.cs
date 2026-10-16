using NotchPeninsula.Plugins;
using SkiaSharp;

namespace NPS.AudioVisualizer;

/// <summary>
/// 详情页：大号频谱 + 设置面板（右键岛上组件展开）。
/// 排版永远按固定 360×192 设计坐标系计算，绘制时整体缩放对齐到当前 rect ——
/// 展开 / 收起的弹簧动画期间 rect 是插值尺寸，拿它排版会每帧跳位。
/// 设置区：四项设置两两一行（名称在左、选项片在右，点击直接选中）；
/// 「无音频时自动隐藏」独占一行且与其他设置同宽，开关靠列右缘；
/// 「重置全部」按钮固定在整行最右侧，与开关之间留出大段空白。
/// </summary>
internal sealed class VisualizerDetailPage : IDetailPage
{
    private const float LW = 360f, LH = 192f;

    // 频谱区（设计坐标系）
    private const float SpecTop = 36f, SpecBottom = 106f;
    private static readonly string[] BandNames = { "低频", "中低", "中频", "中高", "高频" };

    private static readonly SKRect CloseBtn = new(330, 8, 352, 30);

    // ---- 设置区网格（设计坐标系）----
    private const float MarginX = 15f;                              // 左右外边距
    private const float ColGap = 12f;                               // 两列间距
    private const float ColW = (LW - MarginX * 2f - ColGap) * 0.5f; // 列宽（自动隐藏行同宽）
    private const float Row1Top = 124f, Row2Top = 146f, RowH = 18f; // 前两行：四项设置
    private const float AutoTop = 168f, AutoH = 20f;                // 末行：自动隐藏 + 重置
    private const float ChipH = 14f, ChipPadX = 4f, ChipGap = 2.5f;
    private const float AutoDelayChipH = 14f, AutoDelayGap = 2f;

    // 各设置的候选项（与设置中心的选择框同口径）
    private static readonly string[] StyleOptions = { SpectrumPaint.StyleName(0), SpectrumPaint.StyleName(1) };
    private static readonly string[] ThicknessOptions = { BarMetrics.Name(0), BarMetrics.Name(1), BarMetrics.Name(2) };
    private static readonly string[] ColorOptions = { SpectrumPaint.ColorName(0), SpectrumPaint.ColorName(1), SpectrumPaint.ColorName(2) };

    private readonly AudioVisualizerPlugin _plugin;
    private IPluginHost? _host;

    // 点击热区：Draw 每帧按当前字体度量重建（字体可能随主题变化），HitTest 复用最近一帧的结果
    private readonly List<(SKRect Rect, string Action)> _hits = new();

    public VisualizerDetailPage(AudioVisualizerPlugin plugin) => _plugin = plugin;
    internal void Attach(IPluginHost host) => _host = host;

    // 宿主会把宽裁剪到 180~1000、高裁剪到 48~480，360×192 在安全区内
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
        var theme = frame.Theme.TextColor;
        var textColor = theme.WithAlpha(a);
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
        else if (!st.IsLive)
        {
            using var line = new SKPaint { IsAntialias = true, Color = frame.Theme.SubTextColor.WithAlpha((byte)(a * 0.55f)) };
            canvas.DrawRect(x0 - 18f, SpecBottom - 1f, span + 36f, 1f, line);
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

        // ---- 设置区（名称在左、选项片在右，点击直接选中，写入注册表持久化）----
        _hits.Clear();

        using var settingLabel = new SKPaint { IsAntialias = true, TextSize = 9.5f, Typeface = PluginFont.Resolve(), Color = theme.WithAlpha((byte)(a * 0.85f)) };
        using var chipText = new SKPaint { IsAntialias = true, TextSize = 9f, Typeface = PluginFont.Resolve(), TextAlign = SKTextAlign.Center };

        float colL = MarginX, colR = MarginX + ColW + ColGap;
        DrawChoiceRow(canvas, colL, Row1Top, "样式", StyleOptions, st.Style, "style", a, theme, settingLabel, chipText);
        DrawChoiceRow(canvas, colR, Row1Top, "粗细", ThicknessOptions, st.Thickness, "thickness", a, theme, settingLabel, chipText);
        DrawChoiceRow(canvas, colL, Row2Top, "配色", ColorOptions, st.ColorMode, "color", a, theme, settingLabel, chipText);
        DrawChoiceRow(canvas, colR, Row2Top, "灵敏", VisualizerState.GainNames, st.GainIndex, "gain", a, theme, settingLabel, chipText);

        DrawAutoRow(canvas, a, theme, settingLabel, chipText);

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
            using var line = new SKPaint { IsAntialias = true, Color = subColor.WithAlpha((byte)(a * 0.55f)) };
            canvas.DrawRect(lineLeft, cy - 0.5f, lineRight - lineLeft, 1f, line);
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

    /// <summary>一行设置：名称在左，选项片靠列右缘排开；点击选项片直接选中该值。</summary>
    private void DrawChoiceRow(SKCanvas canvas, float left, float top, string label, string[] options, int selected,
        string key, byte a, SKColor theme, SKPaint labelPaint, SKPaint chipPaint)
    {
        float midY = top + RowH * 0.5f;
        canvas.DrawText(label, left, midY + 3.4f, labelPaint);

        // 选项片总宽（含间距），从列右缘往左排 —— 候选值直接点击，不用下拉框
        float total = (options.Length - 1) * ChipGap;
        for (int i = 0; i < options.Length; i++)
            total += chipPaint.MeasureText(options[i]) + ChipPadX * 2f;

        float x = left + ColW - total;
        for (int i = 0; i < options.Length; i++)
        {
            float w = chipPaint.MeasureText(options[i]) + ChipPadX * 2f;
            var r = new SKRect(x, midY - ChipH * 0.5f, x + w, midY + ChipH * 0.5f);
            DrawChip(canvas, r, options[i], i == selected, a, theme, chipPaint);

            // 命中区上下各扩 1，提高小控件的易点性（与相邻行仍留空隙）
            _hits.Add((new SKRect(r.Left, r.Top - 1f, r.Right, r.Bottom + 1f), $"{key}:{i}"));
            x += w + ChipGap;
        }
    }

    /// <summary>末行：自动隐藏设置占左列（与其他设置同宽，开关靠列右缘），「重置全部」固定在整行最右侧。</summary>
    private void DrawAutoRow(SKCanvas canvas, byte a, SKColor theme, SKPaint labelPaint, SKPaint chipPaint)
    {
        var st = _plugin.State;
        float midY = AutoTop + AutoH * 0.5f;

        canvas.DrawText("无音频时自动隐藏", MarginX, midY + 3.4f, labelPaint);

        var sw = new SKRect(MarginX + 137f, midY - 6f, MarginX + 159f, midY + 6f);
        DrawSwitch(canvas, sw, st.AutoHideWhenSilent, a, theme);
        _hits.Add((new SKRect(sw.Left - 2f, sw.Top - 2f, sw.Right + 2f, sw.Bottom + 2f), "autoHide"));

        using var delayPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 9f,
            Typeface = PluginFont.Resolve(),
            TextAlign = SKTextAlign.Center,
        };
        float x = sw.Right + 12f;
        for (int i = 0; i < VisualizerState.AutoHideDelayOptions.Length; i++)
        {
            string text = VisualizerState.AutoHideDelayOptions[i].ToString();
            float w = delayPaint.MeasureText(text) + 5f;
            var r = new SKRect(x, midY - AutoDelayChipH * 0.5f, x + w, midY + AutoDelayChipH * 0.5f);
            DrawChip(canvas, r, text, VisualizerState.AutoHideDelayOptions[i] == st.AutoHideDelaySec, a, theme, delayPaint);
            _hits.Add((new SKRect(r.Left, r.Top - 1f, r.Right, r.Bottom + 1f), $"autoHideDelay:{i}"));
            x += w + AutoDelayGap;
        }

        using var unitPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 9f,
            Typeface = PluginFont.Resolve(),
            Color = theme.WithAlpha((byte)(a * 0.60f)),
        };
        canvas.DrawText("秒", x + 1f, midY + 3.2f, unitPaint);

        float resetW = chipPaint.MeasureText("重置全部") + 12f;
        var reset = new SKRect(LW - MarginX - resetW, midY - 8f, LW - MarginX, midY + 8f);
        DrawChip(canvas, reset, "重置全部", selected: false, a, theme, chipPaint);
        _hits.Add((reset, "reset"));
    }

    /// <summary>选项片：细边框圆角小块；选中项只用更亮的底色强调，与其他控件同色系（不引入彩色）。</summary>
    private static void DrawChip(SKCanvas canvas, SKRect r, string text, bool selected, byte a, SKColor theme, SKPaint tp)
    {
        var rr = new SKRoundRect(r, 4f);
        using (var bg = new SKPaint { IsAntialias = true, Color = theme.WithAlpha((byte)(a * (selected ? 0.22f : 0.08f))) })
            canvas.DrawRoundRect(rr, bg);
        using (var bd = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = theme.WithAlpha((byte)(a * (selected ? 0.34f : 0.16f))) })
            canvas.DrawRoundRect(rr, bd);

        tp.Color = theme.WithAlpha((byte)(a * (selected ? 0.95f : 0.60f)));
        canvas.DrawText(text, r.MidX, r.MidY + 3.2f, tp);
    }

    /// <summary>开关：开启时绿色轨道 + 右侧白点，关闭时中性灰轨道 + 左侧灰点。</summary>
    private static void DrawSwitch(SKCanvas canvas, SKRect r, bool on, byte a, SKColor theme)
    {
        using (var track = new SKPaint
        {
            IsAntialias = true,
            Color = on ? SKColor.Parse("#22C55E").WithAlpha((byte)(a * 0.95f)) : theme.WithAlpha((byte)(a * 0.15f)),
        })
            canvas.DrawRoundRect(new SKRoundRect(r, r.Height * 0.5f), track);

        float d = r.Height - 4f;
        float kx = on ? r.Right - 2f - d : r.Left + 2f;
        using (var knob = new SKPaint
        {
            IsAntialias = true,
            Color = on ? new SKColor(255, 255, 255, (byte)(a * 0.95f)) : theme.WithAlpha((byte)(a * 0.80f)),
        })
            canvas.DrawCircle(kx + d * 0.5f, r.MidY, d * 0.5f, knob);
    }

    public WidgetHit HitTest(float x, float y, SKRect rect)
    {
        if (CloseBtn.Contains(x, y)) return new WidgetHit("close");
        foreach (var (r, action) in _hits)
            if (r.Contains(x, y)) return new WidgetHit(action);
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
                return;
            case "autoHide":
                st.AutoHideWhenSilent = !st.AutoHideWhenSilent;
                h.SetSetting("autoHide", st.AutoHideWhenSilent ? "true" : "false");
                if (!st.AutoHideWhenSilent) _plugin.EnsureVisible(); // 关掉开关立即恢复显示
                return;
            case "reset":
                // 重置全部：五项恢复默认值（柱形 / 标准 / 彩虹色 / ×1 / 关闭自动隐藏）
                bool relayout = st.Thickness != 1;
                st.Style = 0;
                st.Thickness = 1;
                st.ColorMode = 1;
                st.GainIndex = 2;
                st.AutoHideWhenSilent = false;
                st.AutoHideDelaySec = VisualizerState.DefaultAutoHideDelaySec;
                h.SetSetting("style", "0");
                h.SetSetting("thickness", "1");
                h.SetSetting("color", "1");
                h.SetSetting("gainIdx", "2");
                h.SetSetting("autoHide", "false");
                h.SetSetting("autoHideDelay", "1");
                _plugin.EnsureVisible();
                if (relayout) h.InvalidateWidgetLayout(); // 粗细档位变化 → 岛上组件宽度重测
                return;
        }

        // 选项片："key:index"（点击即选中该值，不再是循环切换）
        if (action is not null
            && action.Split(':') is { Length: 2 } kv
            && int.TryParse(kv[1], out int idx)
            && idx >= 0)
        {
            switch (kv[0])
            {
                case "style":
                    st.Style = idx % VisualizerState.StyleCount;
                    h.SetSetting("style", st.Style.ToString());
                    break;
                case "thickness":
                    st.Thickness = idx % 3;
                    h.SetSetting("thickness", st.Thickness.ToString());
                    // 岛组件宽度随粗细变化：宿主缓存了 MeasureWidth，必须通知重测
                    h.InvalidateWidgetLayout();
                    break;
                case "color":
                    st.ColorMode = idx % 3;
                    h.SetSetting("color", st.ColorMode.ToString());
                    break;
                case "gain":
                    // 与设置中心的选择框同口径：统一存索引（"gainIdx"）
                    st.GainIndex = idx % VisualizerState.GainSteps.Length;
                    h.SetSetting("gainIdx", st.GainIndex.ToString());
                    break;
                case "autoHideDelay":
                    st.AutoHideDelaySec = VisualizerState.AutoHideDelayOptions[idx % VisualizerState.AutoHideDelayOptions.Length];
                    h.SetSetting("autoHideDelay", (idx % VisualizerState.AutoHideDelayOptions.Length).ToString());
                    break;
            }
        }
    }
}
