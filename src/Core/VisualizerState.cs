namespace NPS.AudioVisualizer;

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
