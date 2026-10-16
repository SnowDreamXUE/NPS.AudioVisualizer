using NotchPeninsula.Plugins;
using SkiaSharp;

namespace NPS.AudioVisualizer;

/// <summary>
/// 「插件设置中心」（NpsSettingsHub）注入页：声明式控件在上、自绘实时预览在下。
/// 只用宿主已有接口（ISettingsPage / ICustomSettingsPage），不引用设置中心的 dll。
///
/// 值域约定与设置中心一致：选择框统一存**索引**的十进制串；插件自己的详情页按钮
/// 也写同一批键，两处改动通过 SettingsChanged 双向同步。
/// </summary>
internal sealed class VisualizerSettingsPage : ISettingsPage, ICustomSettingsPage
{
    private readonly AudioVisualizerPlugin _plugin;

    public VisualizerSettingsPage(AudioVisualizerPlugin plugin) => _plugin = plugin;

    // ---- 声明式控件（内置 ChoiceSetting，设置中心直接渲染）----
    public string Title => "音频可视化";

    public IReadOnlyList<SettingControl> Controls => new SettingControl[]
    {
        new ChoiceSetting("style",     "频谱样式", new[] { "柱形", "双声道" }, 0),
        new ChoiceSetting("thickness", "柱形粗细", new[] { "细", "标准", "粗" }, 1),
        new ChoiceSetting("color",     "配色",     new[] { "主题色", "彩虹色", "霓虹色" }, 1),
        new ChoiceSetting("gainIdx",   "灵敏度",   new[] { "×0.5", "×0.8", "×1", "×1.5", "×2" }, 2),
        new ToggleSetting("autoHide", "无音频时自动隐藏", false),
        new ChoiceSetting("autoHideDelay", "隐藏延迟（秒）", new[] { "0", "2", "4", "5", "10" }, 1),
    };

    // ---- 自绘实时预览（ICustomSettingsPage）----
    // 数据取共享状态里的平滑值（岛上渲染循环持续喂），设置改动即时可见。
    // 自绘区收不到滚轮 / 键盘，纯展示即可，鼠标三事件留空。

    public float MeasureHeight() => 108f;

    public void Draw(SKCanvas canvas, SKRect rect, RenderTheme theme)
    {
        var st = _plugin.State;
        bool glow = SpectrumPaint.UseGlow(st);
        var text = theme.TextColor;
        var sub = theme.SubTextColor;

        // 说明行（画中文必须借宿主字体，否则全是方框）
        using (var cap = new SKPaint { IsAntialias = true, TextSize = 10.5f, Typeface = PluginFont.Resolve(), Color = sub })
            canvas.DrawText("实时预览 · 数据跟随岛上频谱", rect.Left + 12f, rect.Top + 16f, cap);

        // 预览区：文字下方的一块横条
        float areaTop = rect.Top + 28f, areaBottom = rect.Top + 96f;
        var (barW, gap) = BarMetrics.For(st.Thickness, detail: true);
        float span = VisualizerState.Bands * barW + (VisualizerState.Bands - 1) * gap;
        float x0 = rect.Left + (rect.Width - span) * 0.5f;

        if (st.Style == VisualizerState.StyleDual)
        {
            DrawDualPreview(canvas, x0, span, barW, gap, areaTop, areaBottom, st, text, sub, glow);
        }
        else if (!st.IsLive)
        {
            using var line = new SKPaint { IsAntialias = true, Color = sub.WithAlpha(140) };
            canvas.DrawRect(x0 - 18f, areaBottom - 1f, span + 36f, 1f, line);
        }
        else
        {
            float maxH = areaBottom - areaTop;
            float radius = Math.Min(6f, barW * 0.18f);
            for (int i = 0; i < VisualizerState.Bands; i++)
            {
                float v = st.Smooth[i];
                float h = v * maxH;
                float x = x0 + i * (barW + gap);
                SpectrumPaint.DrawGlowBar(canvas, new SKRect(x, areaBottom - h, x + barW, areaBottom),
                    radius, SpectrumPaint.ColorFor(st, i, text), 255, glow);
            }
        }
    }

    /// <summary>双声道预览：上左下右镜像；无声音时居中一条线。</summary>
    private static void DrawDualPreview(SKCanvas canvas, float x0, float span, float barW, float gap,
        float areaTop, float areaBottom, VisualizerState st, SKColor text, SKColor sub, bool glow)
    {
        float cy = (areaTop + areaBottom) * 0.5f;
        float halfH = (areaBottom - areaTop) * 0.5f - 4f;

        using (var tag = new SKPaint { IsAntialias = true, TextSize = 9.5f, Typeface = PluginFont.ResolveBold(), Color = sub, TextAlign = SKTextAlign.Center })
        {
            canvas.DrawText("L", x0 - 14, areaTop + 9, tag);
            canvas.DrawText("R", x0 - 14, areaBottom, tag);
        }

        if (!st.IsLive)
        {
            using var line = new SKPaint { IsAntialias = true, Color = text.WithAlpha(230) };
            canvas.DrawRect(x0 - 18, cy - 1f, span + 36, 2f, line);
            return;
        }

        using (var sep = new SKPaint { IsAntialias = true, Color = text.WithAlpha(64) })
            canvas.DrawRect(x0 - 18, cy - 0.5f, span + 36, 1f, sep);

        float radius = Math.Min(5f, barW * 0.16f);
        for (int i = 0; i < VisualizerState.Bands; i++)
        {
            float x = x0 + i * (barW + gap);
            float vl = st.SmoothL[i], vr = st.SmoothR[i];
            SpectrumPaint.DrawGlowBar(canvas,
                new SKRect(x, cy - 3f - Math.Max(3f, vl * halfH), x + barW, cy - 3f),
                radius, SpectrumPaint.ColorFor(st, i, text), 255, glow);
            SpectrumPaint.DrawGlowBar(canvas,
                new SKRect(x, cy + 3f, x + barW, cy + 3f + Math.Max(3f, vr * halfH)),
                radius, SpectrumPaint.ColorFor(st, i, text), 255, glow);
        }
    }

    public void OnMouseDown(float x, float y) { }
    public void OnMouseMove(float x, float y) { }
    public void OnMouseUp(float x, float y) { }
}
