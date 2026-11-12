using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using BetterYYSLS.GameTask;
using CommunityToolkit.Mvvm.ComponentModel;
using Fischless.GameCapture;

namespace BetterYYSLS.Core.Config;

/// <summary>
///     更好的燕云十六声配置根。
///     只保留框架运行所需的配置，具体游戏功能请通过新增配置类并在 <see cref="InitEvent"/> 中挂接变更通知。
/// </summary>
[Serializable]
public partial class AllConfig : ObservableObject
{
    /// <summary>
    ///     窗口捕获的方式
    /// </summary>
    [ObservableProperty]
    private string _captureMode = CaptureModes.WindowsGraphicsCapture.ToString();

    /// <summary>
    ///     详细的错误日志
    /// </summary>
    [ObservableProperty]
    private bool _detailedErrorLogs;

    /// <summary>
    ///     不展示新版本提示的最新版本
    /// </summary>
    [ObservableProperty]
    private string _notShowNewVersionNoticeEndVersion = "";

    /// <summary>
    ///     触发器触发频率（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _triggerInterval = 50;

    /// <summary>
    ///     WGC 使用位图缓存。高帧率下可能导致卡顿，出现黑屏时请关闭。
    /// </summary>
    [ObservableProperty]
    private bool _wgcUseBitmapCache = true;

    /// <summary>
    ///     遮罩窗口配置
    /// </summary>
    public MaskWindowConfig MaskWindowConfig { get; set; } = new();

    /// <summary>
    ///     通用配置
    /// </summary>
    public CommonConfig CommonConfig { get; set; } = new();

    /// <summary>
    ///     游戏启动配置
    /// </summary>
    public GameStartConfig GameStartConfig { get; set; } = new();

    /// <summary>
    ///     自动剧情配置
    /// </summary>
    public AutoSkipConfig AutoSkipConfig { get; set; } = new();

    /// <summary>
    ///     大地图窗口配置
    /// </summary>
    public MapWindowConfig MapWindowConfig { get; set; } = new();

    /// <summary>
    ///     示例触发器配置
    /// </summary>
    public SampleTriggerConfig SampleTriggerConfig { get; set; } = new();

    /// <summary>
    ///     占位触发器配置
    /// </summary>
    public PlaceholderTriggerConfig PlaceholderTriggerConfig { get; set; } = new();

    /// <summary>
    ///     快捷键配置
    /// </summary>
    public HotKeyConfig HotKeyConfig { get; set; } = new();

    /// <summary>
    ///     任意配置项变化后的保存回调
    /// </summary>
    [JsonIgnore]
    public Action? OnAnyChangedAction { get; set; }

    /// <summary>
    ///     挂接所有子配置的变更通知
    /// </summary>
    public void InitEvent()
    {
        PropertyChanged += OnAnyPropertyChanged;
        MaskWindowConfig.PropertyChanged += OnAnyPropertyChanged;
        CommonConfig.PropertyChanged += OnAnyPropertyChanged;
        GameStartConfig.PropertyChanged += OnAnyPropertyChanged;
        AutoSkipConfig.PropertyChanged += OnAnyPropertyChanged;
        MapWindowConfig.PropertyChanged += OnAnyPropertyChanged;
        SampleTriggerConfig.PropertyChanged += OnAnyPropertyChanged;
        PlaceholderTriggerConfig.PropertyChanged += OnAnyPropertyChanged;
        HotKeyConfig.PropertyChanged += OnAnyPropertyChanged;
    }

    /// <summary>
    ///     配置变化时刷新触发器状态并触发保存
    /// </summary>
    public void OnAnyPropertyChanged(object? sender, EventArgs args)
    {
        GameTaskManager.RefreshTriggerConfigs();
        OnAnyChangedAction?.Invoke();
    }
}
