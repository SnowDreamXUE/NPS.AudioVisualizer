using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace NPS.AudioVisualizer;

/// <summary>
/// 立体声捕获：插件自带一路 WASAPI Loopback，按左 / 右声道分别跑 5 路 Goertzel，
/// 为「双声道」样式提供真·左右声道频谱（宿主的 WidgetFrame.Bars 只取左声道混成单声道，
/// 给不了右耳数据）。频率点 / 权重 / AGC 思路与宿主 AudioAnalyzer 一致，数据双缓冲无锁交换。
///
/// NAudio 程序集由宿主进程提供（同版本 2.2.1，插件加载器会解析回宿主已加载的程序集），
/// 插件不随带。捕获失败（被独占 / 无音频设备）时 IsRunning=false，调用方回退为镜像单声道。
/// 只在「双声道」样式下运行，由插件入口按 1s 周期启停，避免常驻占用。
/// 切换默认输出设备后三重自愈：RecordingStopped 即时清零（频谱落底而非冻结）→
/// 周期 tick 比对默认设备 ID 立即重建 → 2.5s 断流超时兜底。
/// </summary>
internal sealed class StereoCapture : IDisposable
{
    public const int Bands = 5;
    private const int TargetSampleRate = 8000;
    private const long RestartAttemptTicks = (long)(1.5 * TimeSpan.TicksPerSecond); // 失败重试限速（成功不计时）
    private const float ActivityThreshold = 0.0015f; // 采样峰值阈值 ≈ -56dBFS：数字静音恒为 0，高于此即真实声音（自动隐藏监测用）

    private float[] _frontL = new float[Bands];
    private float[] _frontR = new float[Bands];
    private float[] _backL = new float[Bands];
    private float[] _backR = new float[Bands];
    private readonly float[] _tempL = new float[Bands];
    private readonly float[] _tempR = new float[Bands];

    // 5 个频率点 × 左右声道 的 Goertzel 递推状态
    private readonly (float coeff, float q1, float q2, float weight)[] _stL =
        new (float coeff, float q1, float q2, float weight)[Bands];
    private readonly (float coeff, float q1, float q2, float weight)[] _stR =
        new (float coeff, float q1, float q2, float weight)[Bands];

    private int _sampleCount;
    private int _channels = 2;
    private int _decimation = 1;
    private float _currentPeak = 0.1f; // AGC 峰值追踪

    private readonly object _captureLock = new();
    private WasapiLoopbackCapture? _capture;
    private volatile bool _running;
    private long _lastDataTicks;
    private long _lastAudioTicks;
    private long _lastStartAttempt;
    private int _failCount;
    private string _capturedDeviceId = ""; // 启动时绑定的默认输出设备（感知切换用）

    public bool IsRunning => _running;
    public bool IsAudioActive => _running && DateTime.UtcNow.Ticks - Volatile.Read(ref _lastAudioTicks) < TimeSpan.FromMilliseconds(700).Ticks;

    /// <summary>左 / 右声道频谱（捕获未运行时为 null，调用方回退镜像单声道）。只在 Draw 当帧读取。</summary>
    public float[]? FrontL => _running ? _frontL : null;
    public float[]? FrontR => _running ? _frontR : null;

    /// <summary>启动捕获（幂等，失败按 1.5s 限速重试；成功不计时，切换设备后的重建不会被限流跳过）。在后台线程调用。</summary>
    public void Start()
    {
        lock (_captureLock)
        {
            if (_running) return;
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastStartAttempt < RestartAttemptTicks) return;
            WasapiLoopbackCapture? capture = null;
            try
            {
                // 先记下当前默认设备再构造捕获（这个顺序只会偏旧，最多多重建一次，是安全方向）
                string deviceId = TryGetDefaultRenderDeviceId();
                capture = new WasapiLoopbackCapture(); // 默认捕获系统主混音输出
                ConfigureBands(capture.WaveFormat);
                capture.DataAvailable += OnAudioData;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();
                _capture = capture;
                _running = true;
                _capturedDeviceId = deviceId;
                _failCount = 0;
                Volatile.Write(ref _lastDataTicks, DateTime.UtcNow.Ticks);
                Volatile.Write(ref _lastAudioTicks, 0L); // 新会话不继承旧的活动时间戳
                try { NotchPeninsula.Logger.Info("[AudioVisualizer] 立体声捕获已启动"); } catch { }
            }
            catch (Exception ex)
            {
                try { capture?.Dispose(); } catch { } // 释放半成品，避免泄漏
                _running = false;
                _lastStartAttempt = now; // 失败才计时：1.5s 后由周期 tick 自动重试
                try
                {
                    if (_failCount++ == 0)
                        NotchPeninsula.Logger.Warn("[AudioVisualizer] 立体声捕获启动失败（将限速重试）: " + ex.Message);
                    else
                        NotchPeninsula.Logger.DebugThrottled("[AudioVisualizer] 立体声捕获重试仍未就绪（同类消息已折叠）");
                }
                catch { }
            }
        }
    }

    /// <summary>停止并释放捕获（幂等）。NAudio 的 Dispose 内部对捕获线程 Join() 没有超时，
    /// 设备切换/拆除瞬间可能长时间阻塞 —— 不能卡宿主的定时回调线程，故先解绑事件 + 快速停录，
    /// Dispose 挪到后台线程执行。</summary>
    public void Stop()
    {
        WasapiLoopbackCapture? cap;
        lock (_captureLock)
        {
            cap = _capture;
            _capture = null;
            _running = false;
            if (cap != null)
            {
                try { cap.DataAvailable -= OnAudioData; } catch { }
                try { cap.RecordingStopped -= OnRecordingStopped; } catch { }
            }
            Array.Clear(_frontL, 0, Bands);
            Array.Clear(_frontR, 0, Bands);
        }
        if (cap != null)
        {
            try { cap.StopRecording(); } catch { } // 只改状态不等线程，立即返回
            var c = cap;
            Task.Run(() => { try { c.Dispose(); } catch { } }); // Join 等待放后台
        }
    }

    /// <summary>周期健康检查：捕获停了 / 默认输出设备已切换 / 超 2.5s 没数据（流被系统断开），任一命中即重建。在后台线程调用。</summary>
    public void RestartIfDead()
    {
        if (!_running) return;
        var cap = _capture;
        bool dead = cap == null
            || cap.CaptureState is CaptureState.Stopped or CaptureState.Stopping
            || (DateTime.UtcNow.Ticks - Volatile.Read(ref _lastDataTicks)) / TimeSpan.TicksPerMillisecond > 2500
            || DefaultDeviceChanged();
        if (!dead) return;
        Stop();
        Start();
    }

    /// <summary>默认输出设备是否已不是启动时绑定的那台（枚举查询失败按未变化处理，避免过渡态误重建）。</summary>
    private bool DefaultDeviceChanged()
    {
        if (_capturedDeviceId.Length == 0) return false;
        string current = TryGetDefaultRenderDeviceId();
        return current.Length > 0
            && !string.Equals(current, _capturedDeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetDefaultRenderDeviceId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device is not null ? device.ID : "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// NAudio 捕获线程终止回调：设备被移除/切换、系统断流时，NAudio 在捕获线程里吞掉异常并经此事件上报，
    /// 进程不崩但数据就此停更。此回调可能来自捕获线程或同步上下文，故不取锁 ——
    /// 立即清空前台缓冲（频谱平滑落回基线而非冻结在半空），并把数据时间戳清零，
    /// 让下个周期 tick 立刻判定死亡并重建。
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Volatile.Write(ref _lastDataTicks, 0L);
        Array.Clear(_frontL, 0, Bands);
        Array.Clear(_frontR, 0, Bands);
        try
        {
            NotchPeninsula.Logger.Warn("[AudioVisualizer] 立体声捕获已终止，等待自动重建"
                + (e?.Exception is { } ex ? ": " + ex.Message : string.Empty));
        }
        catch { }
    }

    private void ConfigureBands(WaveFormat format)
    {
        _channels = Math.Max(1, format.Channels);
        _decimation = Math.Max(1, format.SampleRate / TargetSampleRate);
        int actualRate = format.SampleRate / _decimation;

        // 与宿主相同的 5 个律动频率点与高频能量补偿
        float[] freqs = { 80f, 250f, 600f, 1500f, 3500f };
        float[] weights = { 1.0f, 1.8f, 2.8f, 4.5f, 6.5f };
        for (int j = 0; j < Bands; j++)
        {
            float freq = Math.Min(freqs[j], actualRate / 2.2f);
            float k = MathF.Round(freq * 256f / actualRate);
            float coeff = 2f * MathF.Cos(2f * MathF.PI * k / 256f);
            _stL[j] = (coeff, 0, 0, weights[j]);
            _stR[j] = (coeff, 0, 0, weights[j]);
        }
        _sampleCount = 0;
        _currentPeak = 0.1f;
    }

    private void OnAudioData(object? sender, WaveInEventArgs e)
    {
        Volatile.Write(ref _lastDataTicks, DateTime.UtcNow.Ticks);

        var buffer = new WaveBuffer(e.Buffer); // Zero-Copy 把 byte[] 读作 float[]
        int floatCount = e.BytesRecorded / 4;
        int step = _channels * _decimation;
        int limit = _channels >= 2 ? floatCount - 1 : floatCount;

        float peak = 0f;
        for (int i = 0; i < limit; i += step)
        {
            float l = buffer.FloatBuffer[i];
            float r = _channels >= 2 ? buffer.FloatBuffer[i + 1] : l; // 单声道设备：右 = 左

            // 声音活动监测：只看采样峰值，数字静音（含系统静音）恒为 0
            float al = MathF.Abs(l), ar = MathF.Abs(r);
            if (al > peak) peak = al;
            if (ar > peak) peak = ar;

            for (int j = 0; j < Bands; j++)
            {
                ref var sl = ref _stL[j];
                float q0 = l + sl.coeff * sl.q1 - sl.q2;
                sl.q2 = sl.q1; sl.q1 = q0;

                ref var sr = ref _stR[j];
                q0 = r + sr.coeff * sr.q1 - sr.q2;
                sr.q2 = sr.q1; sr.q1 = q0;
            }

            // 每 256 个降采样样本（约 32ms）输出一次能量
            if (++_sampleCount >= 256)
            {
                float maxVal = 0f;
                for (int j = 0; j < Bands; j++)
                {
                    ref var sl = ref _stL[j];
                    float pl = sl.q1 * sl.q1 + sl.q2 * sl.q2 - sl.coeff * sl.q1 * sl.q2;
                    float vl = MathF.Sqrt(Math.Max(0, pl)) / 256f * 20f * sl.weight;
                    _tempL[j] = vl;
                    sl.q1 = 0; sl.q2 = 0;

                    ref var sr = ref _stR[j];
                    float pr = sr.q1 * sr.q1 + sr.q2 * sr.q2 - sr.coeff * sr.q1 * sr.q2;
                    float vr = MathF.Sqrt(Math.Max(0, pr)) / 256f * 20f * sr.weight;
                    _tempR[j] = vr;
                    sr.q1 = 0; sr.q2 = 0;

                    float m = Math.Max(vl, vr);
                    if (m > maxVal) maxVal = m;
                }
                _sampleCount = 0;

                // AGC：快升慢降包络追踪 + 底噪红线 + 动态倍率
                if (maxVal > _currentPeak) _currentPeak = maxVal;
                else _currentPeak *= 0.98f;
                float gain = Math.Clamp(0.9f / Math.Max(_currentPeak, 0.02f), 1f, 30f);
                for (int j = 0; j < Bands; j++)
                {
                    _tempL[j] = Math.Clamp(_tempL[j] * gain, 0f, 1f);
                    _tempR[j] = Math.Clamp(_tempR[j] * gain, 0f, 1f);
                }

                // 无锁双缓冲原子交换（左右各一份）
                Array.Copy(_tempL, _backL, Bands);
                var oldL = Interlocked.Exchange(ref _frontL, _backL);
                Array.Clear(oldL, 0, Bands);
                _backL = oldL;

                Array.Copy(_tempR, _backR, Bands);
                var oldR = Interlocked.Exchange(ref _frontR, _backR);
                Array.Clear(oldR, 0, Bands);
                _backR = oldR;
            }
        }

        // 超过阈值即刷新活动时间戳（IsAudioActive 的判定依据）
        if (peak >= ActivityThreshold)
            Volatile.Write(ref _lastAudioTicks, DateTime.UtcNow.Ticks);
    }

    public void Dispose() => Stop();
}
