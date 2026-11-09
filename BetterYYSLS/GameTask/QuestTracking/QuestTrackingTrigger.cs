using BetterYYSLS.Core.Config;
using BetterYYSLS.Core.Input;
using BetterYYSLS.GameTask.Common;
using BetterYYSLS.Service;
using Microsoft.Extensions.Logging;
using System;
using Vanara.PInvoke;

namespace BetterYYSLS.GameTask.QuestTracking;

/// <summary>
/// 任务追踪触发器。只按 N 追踪当前任务，不打开 J/ESC 菜单，也不处理剧情对话。
/// </summary>
public class QuestTrackingTrigger : ITaskTrigger
{
    private readonly ILogger<QuestTrackingTrigger> _logger = App.GetLogger<QuestTrackingTrigger>();

    /// <summary>本次启用是否已经按过 N</summary>
    private bool _done;

    /// <summary>下一次允许操作的时间</summary>
    private DateTime _nextActionTime = DateTime.MinValue;

    /// <summary>是否已经提示过“指定序号不支持”</summary>
    private bool _indexWarningLogged;

    /// <summary>触发器名称</summary>
    public string Name => "任务追踪";

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>执行优先级低于自动剧情</summary>
    public int Priority => 40;

    /// <summary>不独占</summary>
    public bool IsExclusive => false;

    /// <summary>只在非剧情界面执行</summary>
    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Main;

    /// <summary>从配置同步启用状态</summary>
    public void Init()
    {
        var enabled = ConfigService.Config?.AutoSkipConfig.TrackQuestOnStart == true;
        if (enabled && !IsEnabled)
        {
            _done = false;
        }

        IsEnabled = enabled;
        _indexWarningLogged = false;
        _nextActionTime = DateTime.MinValue;
    }

    /// <summary>
    /// 在非剧情界面按一次 N 追踪当前任务。
    /// </summary>
    public void OnCapture(CaptureContent content)
    {
        if (!IsEnabled || _done)
        {
            return;
        }

        var now = DateTime.Now;
        if (now < _nextActionTime)
        {
            return;
        }

        // 对话中不抢剧情按键
        if (GameUiState.IsInTalkUi(content.CaptureRectArea))
        {
            return;
        }

        _nextActionTime = now.AddMilliseconds(1000);

        if (!SystemControl.IsGameActive())
        {
            SystemControl.ActivateWindow();
            return;
        }

        var config = ConfigService.Config?.AutoSkipConfig;
        if (!_indexWarningLogged && config is { QuestSelectIndex: >= 0 })
        {
            _logger.LogWarning("任务追踪暂不支持指定序号，将按 N 追踪当前任务");
            _indexWarningLogged = true;
        }

        PressForeground(User32.VK.VK_N);
        _done = true;
        _logger.LogInformation("已按 N 追踪当前任务");
    }

    /// <summary>
    /// 发送前台按键，按住 50ms 避免游戏漏键。
    /// </summary>
    private static void PressForeground(User32.VK key)
    {
        InputHub.Foreground.Keyboard.KeyDown(key);
        System.Threading.Thread.Sleep(50);
        InputHub.Foreground.Keyboard.KeyUp(key);
    }
}
