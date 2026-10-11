using SkiaSharp;

namespace NPS.AudioVisualizer;

/// <summary>
/// 字体解析器：直接借宿主的字体，插件不自带任何字体文件。
/// 铁律两条：每次现取、绝不缓存（宿主换字体会 Dispose 旧字体面，缓存即 use-after-dispose）；
/// 绝不 Dispose 借来的字体面（进程级共享资源，释放会拖垮整个岛体）。
/// </summary>
internal static class PluginFont
{
    /// <summary>借一个当前可用的正文字体面；拿不到时返回 null，调用方保持 Skia 默认字体。</summary>
    public static SKTypeface? Resolve()
    {
        try { return NotchPeninsula.FontConfig.Normal; }   // 和岛上其余文字同一套
        catch { }                                          // 旧宿主没有 FontConfig → 降级
        try { return NotchPeninsula.FontConfig.Fallback; }
        catch { return null; }
    }

    /// <summary>借粗体（标题用）；拿不到时退回正文。</summary>
    public static SKTypeface? ResolveBold()
    {
        try { return NotchPeninsula.FontConfig.Bold; }
        catch { return Resolve(); }
    }
}
