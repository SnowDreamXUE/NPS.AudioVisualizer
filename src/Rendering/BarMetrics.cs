namespace NPS.AudioVisualizer;

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
