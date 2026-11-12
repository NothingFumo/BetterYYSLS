using BetterYYSLS.Core.Config;
using BetterYYSLS.Core.Input;
using BetterYYSLS.Core.Recognition;
using BetterYYSLS.Core.Recognition.OCR;
using BetterYYSLS.Core.Recognition.OpenCv;
using BetterYYSLS.GameTask.AutoSkip.Assets;
using BetterYYSLS.GameTask.AutoSkip.Audio;
using BetterYYSLS.GameTask.Common;
using BetterYYSLS.GameTask.Model.Area;
using BetterYYSLS.Helpers;
using BetterYYSLS.Service;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vanara.PInvoke;

namespace BetterYYSLS.GameTask.AutoSkip;

/// <summary>
/// 自动剧情触发器。只处理剧情对话：推进、跳过、继续、跟随和按文字选择选项。
/// 任务追踪由独立的 <see cref="QuestTracking.QuestTrackingTrigger"/> 负责。
/// </summary>
public class AutoSkipTrigger : ITaskTrigger
{
    private readonly ILogger<AutoSkipTrigger> _logger = App.GetLogger<AutoSkipTrigger>();

    /// <summary>下一次允许操作的时间</summary>
    private DateTime _nextActionTime = DateTime.MinValue;

    /// <summary>最近一次识别到“剧情播放中”的时间</summary>
    private DateTime _lastPlayingTime = DateTime.MinValue;

    /// <summary>最近一次确认处于剧情界面的时间，用于黑屏过场点击</summary>
    private DateTime _lastTalkTime = DateTime.MinValue;

    /// <summary>最近一次黑屏点击的时间</summary>
    private DateTime _prevClickTime = DateTime.MinValue;

    /// <summary>下一次允许 OCR 扫描选项的时间</summary>
    private DateTime _nextOptionScanTime = DateTime.MinValue;

    /// <summary>本次剧情是否已按过 R 倍速</summary>
    private bool _speedUpSent;

    /// <summary>语音结束等待器</summary>
    private DialogueOptionAudioWaiter? _voiceWaiter;

    /// <summary>本次选项轮次已经尝试的滚轮次数</summary>
    private int _optionScrollAttempts;

    /// <summary>后台剧情结束后等待切回前台</summary>
    private bool _pendingBringToFront;

    /// <summary>上一次切回前台的时间</summary>
    private DateTime _prevBringToFrontTime = DateTime.MinValue;

    /// <summary>触发器名称</summary>
    public string Name => "自动剧情";

    /// <summary>是否启用，由配置控制</summary>
    public bool IsEnabled { get; set; }

    /// <summary>执行优先级</summary>
    public int Priority => 50;

    /// <summary>不独占，允许与其他触发器同时运行</summary>
    public bool IsExclusive => false;

    /// <summary>只在剧情对话界面执行</summary>
    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Talk;

    /// <summary>游戏失焦时仍保持截图</summary>
    public bool IsBackgroundRunning => true;

    /// <summary>自动剧情配置</summary>
    private static AutoSkipConfig? Config => ConfigService.Config?.AutoSkipConfig;

    /// <summary>
    /// 从配置同步启用状态并重新加载素材
    /// </summary>
    public void Init()
    {
        IsEnabled = Config?.Enabled == true;
        _speedUpSent = false;
        _voiceWaiter?.ReleaseDetector();
        _voiceWaiter = null;
        AutoSkipAssets.DestroyInstance();
    }

    /// <summary>
    /// 每帧识别剧情界面并执行对应操作。
    /// </summary>
    public void OnCapture(CaptureContent content)
    {
        var config = Config;
        if (config is not { Enabled: true })
        {
            return;
        }

        try
        {
            var now = DateTime.Now;
            if (now < _nextActionTime)
            {
                return;
            }

            var area = content.CaptureRectArea;

            // 1. 跳过确认弹窗优先级最高
            if (Exists(area, AutoSkipAssets.Instance.ConfirmRo))
            {
                _lastTalkTime = now;
                Press(User32.VK.VK_SPACE);
                _nextActionTime = now.AddMilliseconds(800);
                return;
            }

            // 2. 剧情暂停，按 T 继续
            if (config.ResumeWhenPaused && Exists(area, AutoSkipAssets.Instance.ResumeRo))
            {
                _lastTalkTime = now;
                Press(User32.VK.VK_T);
                _nextActionTime = now.AddMilliseconds(400);
                return;
            }

            // 3. 可跳过剧情，按 Space 请求跳过
            if (config.SkipCutscene && Exists(area, AutoSkipAssets.Instance.SkipRo))
            {
                _lastTalkTime = now;
                Press(User32.VK.VK_SPACE);
                _nextActionTime = now.AddMilliseconds(800);
                return;
            }

            // 4. 剧情正在播放：先按 R 倍速，再按 Space 推进
            if (Exists(area, AutoSkipAssets.Instance.PauseRo))
            {
                _lastTalkTime = now;
                _lastPlayingTime = now;
                if (config.RunBackgroundEnabled && !SystemControl.IsGameActive())
                {
                    _pendingBringToFront = true;
                }
                if (config.SpeedUpDialogue && !_speedUpSent)
                {
                    Press(User32.VK.VK_R);
                    _speedUpSent = true;
                    _nextActionTime = now.AddMilliseconds(300);
                    return;
                }

                Press(User32.VK.VK_SPACE);
                _nextActionTime = now.AddMilliseconds(700);
                return;
            }

            if ((now - _lastPlayingTime).TotalSeconds > 3)
            {
                _speedUpSent = false;
            }

            // 5. 跟随 NPC
            if (config.FollowNpc && Exists(area, AutoSkipAssets.Instance.FollowRo))
            {
                _lastTalkTime = now;
                Press(User32.VK.VK_G);
                _nextActionTime = now.AddMilliseconds(1000);
                return;
            }

            // 6. 对话选项：OCR 读取文字后按优先级选择
            var hasOptionSelection = config.SelectFirstOption ||
                                     config.SelectLastOption ||
                                     config.SelectRandomOption ||
                                     config.OptionSelectIndex >= 0 ||
                                     !string.IsNullOrWhiteSpace(config.OptionPriorityKeywords) ||
                                     !string.IsNullOrWhiteSpace(config.OptionPauseKeywords);
            if (hasOptionSelection && now >= _nextOptionScanTime)
            {
                _nextOptionScanTime = now.AddMilliseconds(700);
                if (TrySelectOption(area, config))
                {
                    _lastTalkTime = now;
                    _nextActionTime = now.AddMilliseconds(800);
                    return;
                }
            }

            TryBringToFrontAfterBackground(config);

            // 7. 黑屏过场：仅在近期确认过剧情界面时点击
            if (ClickBlackGameScreen(area))
            {
                return;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "自动剧情识别失败");
            _nextActionTime = DateTime.Now.AddMilliseconds(1000);
        }
    }

    /// <summary>
    /// OCR 右侧选项区域，按配置选择第一项、最后一项或指定序号。
    /// </summary>
    private bool TrySelectOption(ImageRegion area, AutoSkipConfig config)
    {
        var width = area.Width;
        var height = area.Height;
        var roi = new Rect(
            (int)(width * 0.55),
            (int)(height * 0.32),
            (int)(width * 0.42),
            (int)(height * 0.52));

        try
        {
            using var mat = new Mat(area.SrcMat, roi);
            var result = OcrService.Instance.OcrResult(mat);
            var options = result.Regions
                .Where(region => ChineseCount(region.Text) >= 2)
                .OrderBy(region => region.Rect.Center.Y)
                .ToList();

            if (options.Count == 0)
            {
                return HandleOptionOcrFailure(area, config);
            }

            _optionScrollAttempts = 0;

            // 参考 BetterGI：优先关键词 > 暂停关键词 > 配置序号/首尾项。
            var priorityKeywords = SplitKeywords(config.OptionPriorityKeywords);
            var pauseKeywords = SplitKeywords(config.OptionPauseKeywords);

            var selected = -1;
            foreach (var keyword in priorityKeywords)
            {
                selected = options.FindIndex(option =>
                    Normalize(option.Text).Contains(Normalize(keyword), StringComparison.Ordinal));
                if (selected >= 0)
                {
                    _logger.LogInformation("自动剧情优先选项命中：{Keyword}", keyword);
                    break;
                }
            }

            if (selected < 0 && pauseKeywords.Count > 0)
            {
                var paused = options.Any(option =>
                    pauseKeywords.Any(keyword =>
                        Normalize(option.Text).Contains(Normalize(keyword), StringComparison.Ordinal)));
                if (paused)
                {
                    _logger.LogInformation("自动剧情选项命中暂停关键词，本轮不选择");
                    return false;
                }
            }

            if (selected < 0)
            {
                selected = config.SelectRandomOption
                    ? Random.Shared.Next(options.Count)
                    : config.SelectLastOption
                        ? options.Count - 1
                        : config.OptionSelectIndex >= 0
                            ? Math.Clamp(config.OptionSelectIndex, 0, options.Count - 1)
                            : 0;
            }

            // 参考 BetterGI 的 VAD 等待：先等待当前语音结束，再点击选项。
            if (config.AutoWaitDialogueOptionVoiceEnabled)
            {
                var waiter = EnsureVoiceWaiter();
                if (waiter is not null)
                {
                    if (waiter.IsWaiting)
                    {
                        if (!waiter.Update(_logger))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        var maxWaitMilliseconds = Math.Clamp(
                            config.DialogueOptionVoiceMaxWaitSeconds,
                            0,
                            600) * 1000;
                        if (waiter.Start(
                                maxWaitMilliseconds,
                                config.AfterChooseOptionSleepDelay,
                                _logger))
                        {
                            return false;
                        }
                    }
                }
            }

            var center = options[selected].Rect.Center;
            ClickAt(area, roi.X + center.X, roi.Y + center.Y);
            _voiceWaiter?.Cancel();
            _logger.LogInformation("自动剧情选择选项：{Text}", options[selected].Text);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "OCR 识别对话选项失败");
            return false;
        }
    }

    /// <summary>
    /// 按换行、逗号或分号拆分关键词并去重。
    /// </summary>
    private static List<string> SplitKeywords(string text)
    {
        return text
            .Split(['\r', '\n', ',', '，', ';', '；', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 去掉空白和标点，避免 OCR 文本与关键词因格式差异无法匹配。
    /// </summary>
    private static string Normalize(string text)
    {
        return new string(text
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character))
            .ToArray());
    }

    /// <summary>
    /// 创建或复用当前游戏进程的 VAD 等待器。
    /// </summary>
    private DialogueOptionAudioWaiter? EnsureVoiceWaiter()
    {
        try
        {
            var processId = TaskContext.Instance().SystemInfo.GameProcessId;
            if (_voiceWaiter is not null)
            {
                return _voiceWaiter;
            }

            _voiceWaiter = new DialogueOptionAudioWaiter(processId);
            return _voiceWaiter;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "自动剧情：创建语音检测器失败，将使用固定延迟");
            return null;
        }
    }

    /// <summary>
    /// OCR 没有读到选项时，先滚动选项列表；确认存在选项气泡后做兜底点击。
    /// </summary>
    private bool HandleOptionOcrFailure(ImageRegion area, AutoSkipConfig config)
    {
        if (!Exists(area, AutoSkipAssets.Instance.OptionFirstRo))
        {
            return false;
        }

        if (config.OptionMaxScrolls > 0 && _optionScrollAttempts < config.OptionMaxScrolls)
        {
            ScrollOptionList(area, config);
            _optionScrollAttempts++;
            return true;
        }

        return ClickFallbackOption(area, config);
    }

    /// <summary>
    /// 在选项区域发送鼠标滚轮。默认看最后一项时向下，否则向上。
    /// </summary>
    private void ScrollOptionList(ImageRegion area, AutoSkipConfig config)
    {
        var channel = config.RunBackgroundEnabled && !SystemControl.IsGameActive()
            ? InputHub.Background
            : InputHub.Foreground;
        var clicks = config.SelectLastOption ? -1 : 1;
        MoveToCapture(channel, area, area.Width * 0.75, area.Height * 0.58);
        channel.Mouse.VerticalScroll(clicks);
        Thread.Sleep(Math.Clamp(config.OptionScrollStepMs, 50, 1000));
        _logger.LogDebug("自动剧情：OCR 未读到选项，滚动选项列表 {Direction}", clicks < 0 ? "向下" : "向上");
    }

    /// <summary>
    /// OCR 完全失败时的气泡兜底点击。
    /// </summary>
    private bool ClickFallbackOption(ImageRegion area, AutoSkipConfig config)
    {
        using var firstOption = area.Find(AutoSkipAssets.Instance.OptionFirstRo);
        if (firstOption.IsExist() && (config.SelectFirstOption || config.SelectRandomOption))
        {
            var center = new Point(firstOption.X + (firstOption.Width / 2d), firstOption.Y + (firstOption.Height / 2d));
            ClickAt(area, center.X, center.Y);
            _logger.LogInformation("自动剧情：OCR 失败，点击第一个选项气泡");
            return true;
        }

        var index = config.SelectRandomOption ? Random.Shared.Next(0, 3) : Math.Max(0, config.OptionSelectIndex);
        var ratio = config.SelectLastOption
            ? 0.70
            : 0.56 + (Math.Clamp(index, 0, 3) * 0.055);
        ClickAt(area, area.Width * 0.75, area.Height * ratio);
        _logger.LogInformation("自动剧情：OCR 失败，按位置点击选项");
        return true;
    }

    /// <summary>
    /// 后台剧情结束超过 3 秒后切回游戏前台。
    /// </summary>
    private void TryBringToFrontAfterBackground(AutoSkipConfig config)
    {
        if (!config.BringGameToFrontAfterBackgroundDialogEnabled ||
            !_pendingBringToFront ||
            SystemControl.IsGameActive())
        {
            return;
        }

        var now = DateTime.Now;
        if ((now - _lastPlayingTime).TotalSeconds <= 3 ||
            (now - _prevBringToFrontTime).TotalSeconds <= 3)
        {
            return;
        }

        _prevBringToFrontTime = now;
        _pendingBringToFront = false;
        SystemControl.ActivateWindow();
        _logger.LogInformation("自动剧情：后台对话结束，已切回游戏前台");
    }

    /// <summary>
    /// 把截图区域坐标移动到屏幕绝对坐标，供鼠标点击或滚轮使用。
    /// </summary>
    private static void MoveToCapture(IInputChannel channel, ImageRegion area, double x, double y)
    {
        var capture = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var screen = PrimaryScreen.WorkingArea;
        var screenX = capture.X + (x * capture.Width / area.Width);
        var screenY = capture.Y + (y * capture.Height / area.Height);
        channel.Mouse.MoveMouseTo(
            screenX * 65535d / screen.Width,
            screenY * 65535d / screen.Height);
    }

    /// <summary>
    /// 将 1080P 区域坐标映射到屏幕并点击。
    /// </summary>
    private void ClickAt(ImageRegion area, double x, double y)
    {
        var useBackground = Config?.RunBackgroundEnabled == true && !SystemControl.IsGameActive();
        if (useBackground)
        {
            MoveToCapture(InputHub.Background, area, x, y);
            InputHub.Background.Mouse.LeftButtonClick();
            return;
        }

        if (!SystemControl.IsGameActive())
        {
            SystemControl.ActivateWindow();
            _nextActionTime = DateTime.Now.AddMilliseconds(300);
            return;
        }

        MoveToCapture(InputHub.Foreground, area, x, y);
        InputHub.Foreground.Mouse.LeftButtonClick();
    }

    /// <summary>
    /// 查找模板并释放结果区域。
    /// </summary>
    private static bool Exists(ImageRegion area, RecognitionObject ro)
    {
        using var ra = area.Find(ro);
        return ra.IsExist();
    }

    /// <summary>
    /// 黑屏剧情点击。取画面中间三分之一，纯黑比例大于 50% 且不接近全黑时点击。
    /// </summary>
    private bool ClickBlackGameScreen(ImageRegion area)
    {
        if ((DateTime.Now - _prevClickTime).TotalMilliseconds <= 1200)
        {
            return false;
        }

        if (_lastTalkTime == DateTime.MinValue ||
            (DateTime.Now - _lastTalkTime).TotalSeconds > 10)
        {
            return false;
        }

        try
        {
            var gray = area.SrcGreyMat;
            var middleHeight = gray.Height / 3;
            using var middle = new Mat(gray, new Rect(0, middleHeight, gray.Width, middleHeight));
            var rate = OpenCvCommonHelper.CountGrayMatColor(middle, 0) * 1d
                       / (middle.Width * middle.Height);

            if (rate is < 0.5 or >= 0.98999)
            {
                return false;
            }

            if (!SystemControl.IsGameActive())
            {
                SystemControl.ActivateWindow();
                _nextActionTime = DateTime.Now.AddMilliseconds(300);
                return true;
            }

            InputHub.Foreground.Mouse.LeftButtonClick();
            _prevClickTime = DateTime.Now;
            _logger.LogDebug("自动剧情：点击黑屏，比例 {Rate:F2}", rate);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "黑屏检测失败");
            return false;
        }
    }

    /// <summary>
    /// 统计文本中的汉字数量。
    /// </summary>
    private static int ChineseCount(string text)
    {
        return text.Count(c => c is >= '\u4e00' and <= '\u9fff');
    }

    /// <summary>
    /// 发送后台按键。
    /// </summary>
    private void Press(User32.VK key)
    {
        var useBackground = Config?.RunBackgroundEnabled == true && !SystemControl.IsGameActive();
        (useBackground ? InputHub.Background : InputHub.Foreground).Keyboard.KeyPress(key);
        _logger.LogDebug("自动剧情按键：{Key}", key);
    }
}
