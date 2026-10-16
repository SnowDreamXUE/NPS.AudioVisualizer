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
    public static readonly string[] GainNames = { "×0.5", "×0.8", "×1", "×1.5", "×2" };
    public static readonly int[] AutoHideDelayOptions = { 0, 2, 4, 5, 10 };
    public const int DefaultAutoHideDelaySec = 2;

    // 设置项（写：点击回调 / 设置变更事件；读：渲染线程。volatile 保证跨线程可见性）
    public volatile int Style;
    public volatile int Thickness = 1;  // 0 细 / 1 标准 / 2 粗
    public volatile int ColorMode;      // 0 主题 / 1 彩虹 / 2 霓虹
    public volatile int GainIndex = 2;  // 默认 ×1
    public volatile bool AutoHideWhenSilent;
    public volatile int AutoHideDelaySec = DefaultAutoHideDelaySec;

    /// <summary>当前灵敏度倍率（由档位索引查表）。</summary>
    public float Gain => GainSteps[GainIndex];

    public readonly float[] Smooth = new float[Bands];  // 单声道（宿主 Bars）
    public readonly float[] SmoothL = new float[Bands]; // 左声道
    public readonly float[] SmoothR = new float[Bands]; // 右声道
    public readonly float[] Peak = new float[Bands];
    private readonly int[] _peakHold = new int[Bands];
    private long _lastFeedTick;

    // 陈旧数据兜底：目标值连续 StaleForceMs 逐位完全不变且非全零 → 上游大概率冻结（捕获死亡残留 / 宿主 Bars 陈旧），
    // 强制清零让频谱平滑落回基线 —— 活音频经 AGC/浮点运算后逐位不变几乎不可能，正常播放不会误触发。
    private readonly float[] _prevM = new float[Bands];
    private readonly float[] _prevL = new float[Bands];
    private readonly float[] _prevR = new float[Bands];
    private readonly float[] _targetM = new float[Bands];
    private readonly float[] _targetL = new float[Bands];
    private readonly float[] _targetR = new float[Bands];
    private long _staleSince;
    private const long StaleForceMs = 600;

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
        bool changed = false, anyLive = false;
        for (int i = 0; i < Bands; i++)
        {
            float m = mono is not null && i < mono.Length ? mono[i] : 0f;
            float l = left is not null && i < left.Length ? left[i] : m;
            float r = right is not null && i < right.Length ? right[i] : m;
            m *= gain; l *= gain; r *= gain;

            if (m != _prevM[i] || l != _prevL[i] || r != _prevR[i]) changed = true;
            if (m > 0.02f || l > 0.02f || r > 0.02f) anyLive = true;
            _prevM[i] = m; _prevL[i] = l; _prevR[i] = r;
            _targetM[i] = m; _targetL[i] = l; _targetR[i] = r;
        }

        // 冻结兜底：所有目标值持续逐位不变且非全零 → 视为上游卡死，强制清零（频谱落底，绝不冻结在半空）
        if (changed || !anyLive) _staleSince = 0;
        else if (_staleSince == 0) _staleSince = now;
        if (_staleSince != 0 && now - _staleSince >= StaleForceMs)
        {
            for (int i = 0; i < Bands; i++) { _targetM[i] = 0f; _targetL[i] = 0f; _targetR[i] = 0f; }
        }

        for (int i = 0; i < Bands; i++)
        {
            Smooth[i] = Step(Smooth[i], _targetM[i]);
            SmoothL[i] = Step(SmoothL[i], _targetL[i]);
            SmoothR[i] = Step(SmoothR[i], _targetR[i]);

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
