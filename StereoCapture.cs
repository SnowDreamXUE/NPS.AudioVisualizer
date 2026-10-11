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
/// </summary>
internal sealed class StereoCapture : IDisposable
{
    public const int Bands = 5;
    private const int TargetSampleRate = 8000;
    private const long RestartAttemptTicks = 3 * TimeSpan.TicksPerSecond; // 失败重试限速

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
    private long _lastStartAttempt;
    private int _failCount;

    public bool IsRunning => _running;

    /// <summary>左 / 右声道频谱（捕获未运行时为 null，调用方回退镜像单声道）。只在 Draw 当帧读取。</summary>
    public float[]? FrontL => _running ? _frontL : null;
    public float[]? FrontR => _running ? _frontR : null;

    /// <summary>启动捕获（幂等，失败按 3s 限速重试）。在后台线程调用。</summary>
    public void Start()
    {
        lock (_captureLock)
        {
            if (_running) return;
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastStartAttempt < RestartAttemptTicks) return;
            _lastStartAttempt = now;

            WasapiLoopbackCapture? capture = null;
            try
            {
                capture = new WasapiLoopbackCapture(); // 默认捕获系统主混音输出
                ConfigureBands(capture.WaveFormat);
                capture.DataAvailable += OnAudioData;
                capture.StartRecording();
                _capture = capture;
                _running = true;
                _failCount = 0;
                Volatile.Write(ref _lastDataTicks, DateTime.UtcNow.Ticks);
                try { NotchPeninsula.Logger.Info("[AudioVisualizer] 立体声捕获已启动"); } catch { }
            }
            catch (Exception ex)
            {
                try { capture?.Dispose(); } catch { } // 释放半成品，避免泄漏
                _running = false;
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

    /// <summary>停止并释放捕获（幂等）。</summary>
    public void Stop()
    {
        lock (_captureLock)
        {
            var cap = _capture;
            _capture = null;
            _running = false;
            if (cap != null)
            {
                try { cap.DataAvailable -= OnAudioData; } catch { }
                try { cap.Dispose(); } catch { }
            }
            Array.Clear(_frontL, 0, Bands);
            Array.Clear(_frontR, 0, Bands);
        }
    }

    /// <summary>周期健康检查：捕获停了或超过 2.5s 没数据（流被系统断开）就重建。在后台线程调用。</summary>
    public void RestartIfDead()
    {
        if (!_running) return;
        var cap = _capture;
        bool dead = cap == null
            || cap.CaptureState is CaptureState.Stopped or CaptureState.Stopping
            || (DateTime.UtcNow.Ticks - Volatile.Read(ref _lastDataTicks)) / TimeSpan.TicksPerMillisecond > 2500;
        if (!dead) return;
        Stop();
        Start();
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

        for (int i = 0; i < limit; i += step)
        {
            float l = buffer.FloatBuffer[i];
            float r = _channels >= 2 ? buffer.FloatBuffer[i + 1] : l; // 单声道设备：右 = 左

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
    }

    public void Dispose() => Stop();
}
