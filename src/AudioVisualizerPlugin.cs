using NotchPeninsula.Plugins;

namespace NPS.AudioVisualizer;

/// <summary>
/// 插件入口：音频可视化（系统输出频谱）。
/// 单声道数据来源是宿主每帧下发的 WidgetFrame.Bars（WASAPI Loopback → 5 频段 0~1）；
/// 「双声道」样式另由 StereoCapture 自开一路 Loopback 拿真·左右声道（宿主 Bars 只含左声道混音）。
/// 设置同时注入「插件设置中心」（RegisterSettingsPage）与自带详情页，两处读写同一份注册表键值。
/// </summary>
public sealed class AudioVisualizerPlugin : INotchPlugin, IDisposable
{
    private IPluginHost? _host;
    private IDisposable? _stereoTimer;
    internal readonly VisualizerState State = new();
    internal readonly StereoCapture Stereo = new();

    // 自动隐藏状态机：静音满 HideDelayMs 后隐藏（宽度归零），有声音立即恢复；由 1s 周期 tick 驱动
    private const long HideDelayMs = 1500;
    private volatile bool _widgetHidden;
    private long _silentSince;

    /// <summary>自动隐藏是否正在生效（组件宽度归零、不占岛上空间）。</summary>
    internal bool WidgetHidden => _widgetHidden;

    // 反域名风格唯一 Id；DisplayName / Version / Author 显示在「我的插件」列表里
    public string Id => "com.nps.audiovisualizer";
    public string DisplayName => "音频可视化";
    public string Version => "1.4.0";
    public string Author => "SnowDream";

    public void Initialize(IPluginHost host)
    {
        _host = host;
        ReadSettings(host);
        host.SettingsChanged += OnSettingsChanged;

        // 注册组件（组件内部自带详情页，右键展开）
        host.RegisterWidget(new VisualizerWidget(this));

        // 把设置注入「插件设置中心」（NpsSettingsHub）：声明式控件 + 自绘预览，只注入一次
        host.RegisterSettingsPage(new VisualizerSettingsPage(this));

        // 立体声捕获按需启停：仅「双声道」样式或开启自动隐藏时运行，后台线程周期检查（含断流自愈 + 自动隐藏判定）
        _stereoTimer = host.ScheduleRefresh(TimeSpan.FromSeconds(1), TickStereoCapture);

        host.PostReminder(new ReminderData
        {
            Title = "音频可视化已加载",
            Body = "右键岛上的频谱组件，可调整样式 / 粗细 / 配色 / 灵敏度，并可开启无音频自动隐藏",
        });
        try { NotchPeninsula.Logger.Info("[AudioVisualizer] loaded v1.4.0"); } catch { }
    }

    /// <summary>捕获的按需启停：双声道样式要真·左右声道数据；自动隐藏开启后组件隐藏了就没有宿主帧，也需要独立监测。顺带驱动自动隐藏判定。</summary>
    private void TickStereoCapture()
    {
        bool want = State.Style == VisualizerState.StyleDual || State.AutoHideWhenSilent;
        if (want && !Stereo.IsRunning) Stereo.Start();
        else if (!want && Stereo.IsRunning) Stereo.Stop();
        else if (want) Stereo.RestartIfDead();

        UpdateAutoHide();
    }

    /// <summary>
    /// 自动隐藏状态机（1s tick 驱动）：静音满 HideDelayMs 后隐藏（宽度归零收回空位），有声音立即恢复。
    /// 只有捕获监测在跑才允许隐藏 —— 否则隐藏后没有任何数据源能触发恢复。
    /// </summary>
    private void UpdateAutoHide()
    {
        if (!State.AutoHideWhenSilent)
        {
            _silentSince = 0;
            EnsureVisible();
            return;
        }

        // 组件可见时优先用频谱状态（含宿主帧），隐藏后只剩独立监测
        if (Stereo.IsAudioActive || State.IsLive)
        {
            _silentSince = 0;
            EnsureVisible();
            return;
        }

        if (!Stereo.IsRunning)
        {
            _silentSince = 0; // 监测还没起来：保持可见，等 Start 的限速重试成功后再判定
            return;
        }

        long now = Environment.TickCount64;
        if (_silentSince == 0) _silentSince = now;
        if (!_widgetHidden && now - _silentSince >= HideDelayMs)
        {
            _widgetHidden = true;
            try { _host?.InvalidateWidgetLayout(); } catch { }
        }
    }

    /// <summary>立即恢复组件显示（关闭自动隐藏 / 重置 / 声音恢复时调用）。</summary>
    internal void EnsureVisible()
    {
        if (!_widgetHidden) return;
        _widgetHidden = false;
        try { _host?.InvalidateWidgetLayout(); } catch { }
    }

    private void OnSettingsChanged()
    {
        if (_host is { } h) ReadSettings(h);
    }

    /// <summary>从注册表读回持久化设置（键由宿主自动加 Plugin.&lt;Id&gt;. 前缀隔离）。</summary>
    internal void ReadSettings(IPluginHost host)
    {
        State.Style = ParseIndex(host.GetSetting("style", "0"), VisualizerState.StyleCount);
        State.ColorMode = ParseIndex(host.GetSetting("color", "1"), 3);
        State.GainIndex = ReadGainIndex(host);
        State.AutoHideWhenSilent = IsTrue(host.GetSetting("autoHide", "false"));

        int thick = ParseIndex(host.GetSetting("thickness", "1"), 3);
        if (thick != State.Thickness)
        {
            State.Thickness = thick;
            // 设置中心等外部途径改了粗细：岛上组件宽度随档位变化，必须通知宿主重测
            try { host.InvalidateWidgetLayout(); } catch { }
        }
    }

    /// <summary>
    /// 读灵敏度档位（索引）。≤1.2.0 版本把浮点倍率存在旧键 "gain" 里（"1"/"1.5"/"3"...），
    /// 这里做一次性迁移到新索引键 "gainIdx"（设置中心的选择框统一写索引）。
    /// </summary>
    private static int ReadGainIndex(IPluginHost host)
    {
        string raw = host.GetSetting("gainIdx", "");
        if (int.TryParse(raw, out int i) && i >= 0 && i < VisualizerState.GainSteps.Length) return i;

        float.TryParse(host.GetSetting("gain", "1"), out float v);
        int best = 2; float bestDist = float.MaxValue;
        for (int j = 0; j < VisualizerState.GainSteps.Length; j++)
        {
            float d = MathF.Abs(VisualizerState.GainSteps[j] - v);
            if (d < bestDist) { bestDist = d; best = j; }
        }
        try { host.SetSetting("gainIdx", best.ToString()); } catch { } // 迁移落盘（幂等，不会循环）
        return best;
    }

    private static int ParseIndex(string s, int mod)
        => int.TryParse(s, out int v) ? ((v % mod) + mod) % mod : 0;

    /// <summary>开关设置统一按 true / 1（忽略大小写）判定为开。</summary>
    private static bool IsTrue(string s)
        => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";

    public void Dispose()
    {
        if (_host is { } h) h.SettingsChanged -= OnSettingsChanged;
        _stereoTimer?.Dispose();
        Stereo.Dispose();
    }
}
