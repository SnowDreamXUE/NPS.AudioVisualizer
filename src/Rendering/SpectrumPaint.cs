using NotchPeninsula.Plugins;
using SkiaSharp;

namespace NPS.AudioVisualizer;

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
