using BetterYYSLS.Core.Config;
using BetterYYSLS.Core.Input;
using BetterYYSLS.Core.Recognition;
using BetterYYSLS.Core.Recognition.OCR;
using BetterYYSLS.Core.Recognition.OpenCv;
using BetterYYSLS.GameTask.AutoSkip.Assets;
using BetterYYSLS.GameTask.Common;
using BetterYYSLS.GameTask.Model.Area;
using BetterYYSLS.Helpers;
using BetterYYSLS.Service;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Linq;
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

            // 6. 对话选项：OCR 读取文字后点击对应选项
            if (config.SelectFirstOption && now >= _nextOptionScanTime)
            {
                _nextOptionScanTime = now.AddMilliseconds(700);
                if (TrySelectOption(area, config))
                {
                    _lastTalkTime = now;
                    _nextActionTime = now.AddMilliseconds(800);
                    return;
                }
            }

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
                return false;
            }

            var index = config.SelectLastOption
                ? options.Count - 1
                : config.OptionSelectIndex >= 0
                    ? Math.Clamp(config.OptionSelectIndex, 0, options.Count - 1)
                    : 0;

            var center = options[index].Rect.Center;
            ClickAt(area, roi.X + center.X, roi.Y + center.Y);
            _logger.LogInformation("自动剧情选择选项：{Text}", options[index].Text);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "OCR 识别对话选项失败");
            return false;
        }
    }

    /// <summary>
    /// 将 1080P 区域坐标映射到屏幕并点击。
    /// </summary>
    private void ClickAt(ImageRegion area, double x, double y)
    {
        if (!SystemControl.IsGameActive())
        {
            SystemControl.ActivateWindow();
            _nextActionTime = DateTime.Now.AddMilliseconds(300);
            return;
        }

        var capture = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var screen = PrimaryScreen.WorkingArea;
        var screenX = capture.X + x * capture.Width / area.Width;
        var screenY = capture.Y + y * capture.Height / area.Height;
        InputHub.Foreground.Mouse.MoveMouseTo(
            screenX * 65535d / screen.Width,
            screenY * 65535d / screen.Height);
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
        InputHub.Background.Keyboard.KeyPress(key);
        _logger.LogDebug("自动剧情按键：{Key}", key);
    }
}
