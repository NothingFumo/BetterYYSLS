using BetterYYSLS.Core.Config;
using BetterYYSLS.Core.Input;
using BetterYYSLS.Core.Recognition;
using BetterYYSLS.Core.Recognition.OpenCv;
using BetterYYSLS.GameTask.AutoSkip.Assets;
using BetterYYSLS.GameTask.Model.Area;
using BetterYYSLS.Helpers;
using BetterYYSLS.Service;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;
using Vanara.PInvoke;

namespace BetterYYSLS.GameTask.AutoSkip;

/// <summary>
/// 自动剧情触发器。
/// 按《燕云十六声》剧情界面的按键提示执行：
/// J 打开任务界面、Space 跳过/确认/追踪、T 继续/暂停、R 倍速、G 跟随、F 选择选项。
/// 任务列表需要鼠标滚轮滚动后点击任务行，因此该流程使用前台输入。
/// </summary>
public class AutoSkipTrigger : ITaskTrigger
{
    /// <summary>对话选项列表选择阶段</summary>
    private enum OptionState
    {
        /// <summary>空闲</summary>
        Idle,

        /// <summary>滚到列表顶部</summary>
        ScrollTop,

        /// <summary>从顶部滚到目标项</summary>
        ScrollToTarget,

        /// <summary>按 F 确认</summary>
        Confirm,
    }

    /// <summary>任务界面操作阶段</summary>
    private enum QuestState
    {
        /// <summary>等待判断任务界面是否已打开</summary>
        Detect,

        /// <summary>已按 J，等待界面加载</summary>
        Opening,

        /// <summary>点击任务分类页签</summary>
        Tab,

        /// <summary>滚到任务列表顶部</summary>
        ScrollTop,

        /// <summary>从顶部滚到指定任务</summary>
        ScrollToTarget,

        /// <summary>点击目标任务行</summary>
        Click,

        /// <summary>按 N 追踪任务</summary>
        Track,

        /// <summary>按 Space 返回任务</summary>
        Return,

        /// <summary>按 Q 关闭任务界面</summary>
        Close,

        /// <summary>任务追踪流程结束</summary>
        Done,
    }

    /// <summary>任务分类数量：进行中、主章、残章、奇遇、探游</summary>
    private const int QuestTabCount = 5;

    /// <summary>向左切换任务分类的最大次数，用于回到最左侧的“进行中”</summary>
    private const int QuestTabResetMoves = 5;

    /// <summary>任务列表第一行的相对点击位置</summary>
    private const double QuestRowX = 0.18;
    private const double QuestRowY = 0.19;

    /// <summary>对话选项列表中心的相对位置</summary>
    private const double OptionWheelX = 0.76;
    private const double OptionWheelY = 0.70;

    /// <summary>任务列表中心的相对位置</summary>
    private const double QuestWheelX = 0.20;
    private const double QuestWheelY = 0.45;

    private readonly ILogger<AutoSkipTrigger> _logger = App.GetLogger<AutoSkipTrigger>();

    /// <summary>下一次允许操作的时间</summary>
    private DateTime _nextActionTime = DateTime.MinValue;

    /// <summary>最近一次识别到“剧情播放中”的时间</summary>
    private DateTime _lastPlayingTime = DateTime.MinValue;

    /// <summary>最近一次识别到任意剧情界面的时间，用于避免剧情刚结束时误开任务界面</summary>
    private DateTime _lastTalkTime = DateTime.MinValue;

    /// <summary>最近一次黑屏点击的时间</summary>
    private DateTime _prevClickTime = DateTime.MinValue;

    /// <summary>任务界面是否已点击过“任务”菜单</summary>
    private bool _questMenuClicked;

    /// <summary>本次剧情是否已按过 R 倍速</summary>
    private bool _speedUpSent;

    /// <summary>当前选项选择阶段</summary>
    private OptionState _optionState = OptionState.Idle;

    /// <summary>选项列表当前阶段已滚动格数</summary>
    private int _optionSteps;

    /// <summary>任务追踪阶段</summary>
    private QuestState _questState = QuestState.Detect;

    /// <summary>任务界面打开尝试次数</summary>
    private int _questAttempts;

    /// <summary>任务列表当前阶段已滚动格数</summary>
    private int _questSteps;

    /// <summary>任务分类切换步数</summary>
    private int _tabSteps;

    /// <summary>是否已通过 Q 回到最左侧分类</summary>
    private bool _tabResetDone;

    /// <summary>本次启用是否已完成任务追踪</summary>
    private bool _questDone;

    /// <summary>最近一次任务面板亮度比例，用于失败诊断</summary>
    private double _lastPanelRatio;

    /// <summary>触发器名称</summary>
    public string Name => "自动剧情";

    /// <summary>是否启用，由配置控制</summary>
    public bool IsEnabled { get; set; }

    /// <summary>执行优先级</summary>
    public int Priority => 50;

    /// <summary>不独占，允许与其他触发器同时运行</summary>
    public bool IsExclusive => false;

    /// <summary>
    /// 游戏失焦时仍保持截图，避免查看日志期间识别使用旧帧。
    /// 任务列表的滚轮和点击仍会在执行前激活游戏窗口。
    /// </summary>
    public bool IsBackgroundRunning => true;

    /// <summary>自动剧情配置</summary>
    private static AutoSkipConfig? Config => ConfigService.Config?.AutoSkipConfig;

    /// <summary>
    /// 从配置同步启用状态，并在每次启动/配置变化时重新加载素材
    /// </summary>
    public void Init()
    {
        var config = Config;
        var enabled = config?.Enabled == true;

        // 仅在“停用 → 启用”时重置任务追踪，避免修改任意配置都重新打开任务界面
        if (enabled && !IsEnabled)
        {
            ResetQuestTracking(config);
        }
        else if (!enabled)
        {
            ResetQuestTracking(config);
        }

        IsEnabled = enabled;
        _speedUpSent = false;
        _optionState = OptionState.Idle;
        _optionSteps = 0;
        AutoSkipAssets.DestroyInstance();
    }

    /// <summary>
    /// 重置任务追踪状态
    /// </summary>
    private void ResetQuestTracking(AutoSkipConfig? config)
    {
        _questState = QuestState.Detect;
        _questAttempts = 0;
        _questSteps = 0;
        _tabSteps = 0;
        _tabResetDone = false;
        _questMenuClicked = false;
        _questDone = config?.TrackQuestOnStart != true;
        _nextActionTime = DateTime.MinValue;
    }

    /// <summary>
    /// 每帧识别剧情界面并执行对应操作。识别与按键都在本方法内完成，不阻塞调度线程。
    /// </summary>
    /// <param name="content">当前帧截图</param>
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

            // 统一按时间点调度，避免连按；每个动作自行设置下一次允许时间
            if (now < _nextActionTime)
            {
                return;
            }

            var area = content.CaptureRectArea;

            // 1. 跳过确认弹窗优先级最高，避免确认框停留时继续触发跳过
            if (Exists(area, AutoSkipAssets.Instance.ConfirmRo))
            {
                _lastTalkTime = now;
                Press(User32.VK.VK_SPACE);
                _nextActionTime = now.AddMilliseconds(800);
                return;
            }

            // 2. 剧情暂停，按 T 继续
            if (config.ResumeWhenPaused &&
                Exists(area, AutoSkipAssets.Instance.ResumeRo))
            {
                _lastTalkTime = now;
                Press(User32.VK.VK_T);
                _nextActionTime = now.AddMilliseconds(400);
                return;
            }

            // 3. 可跳过剧情，按 Space 请求跳过
            if (config.SkipCutscene &&
                Exists(area, AutoSkipAssets.Instance.SkipRo))
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

            // 播放提示消失一段时间后，允许下一次剧情重新按 R
            if ((now - _lastPlayingTime).TotalSeconds > 3)
            {
                _speedUpSent = false;
            }

            // 5. 跟随 NPC
            if (config.FollowNpc &&
                Exists(area, AutoSkipAssets.Instance.FollowRo))
            {
                _lastTalkTime = now;
                Press(User32.VK.VK_G);
                _nextActionTime = now.AddMilliseconds(1000);
                return;
            }

            // 6. 对话选项：滚轮回到顶部，滚到目标项后按 F 确认
            if (config.SelectFirstOption &&
                Exists(area, AutoSkipAssets.Instance.OptionFirstRo))
            {
                _lastTalkTime = now;
                HandleOptionSelection(config);
                return;
            }

            _optionState = OptionState.Idle;
            _optionSteps = 0;

            // 7. 黑屏剧情：中间区域大面积纯黑时点击鼠标推进
            if (ClickBlackGameScreen(area))
            {
                return;
            }

            // 8. 剧情结束后才处理任务追踪，避免在对话界面误开任务面板
            if (!_questDone && config.TrackQuestOnStart &&
                (now - _lastTalkTime).TotalSeconds > 3)
            {
                // 不指定任务序号时只按 N 追踪当前任务，不打开任务界面
                if (config.QuestSelectIndex < 0)
                {
                    if (!SystemControl.IsGameActive())
                    {
                        SystemControl.ActivateWindow();
                        _nextActionTime = now.AddMilliseconds(300);
                        return;
                    }

                    PressForeground(User32.VK.VK_N);
                    _questDone = true;
                    _nextActionTime = now.AddMilliseconds(500);
                    _logger.LogInformation("已按 N 追踪当前任务");
                    return;
                }

                HandleQuestTracking(area, config);
            }
        }
        catch (Exception e)
        {
            // 素材未就绪或识别失败时只记录日志，避免中断整个调度循环
            _logger.LogWarning(e, "自动剧情识别失败");
            _nextActionTime = DateTime.Now.AddMilliseconds(1000);
        }
    }

    /// <summary>
    /// 查找模板并释放结果区域，避免每帧持有未释放的 Region。
    /// </summary>
    private static bool Exists(ImageRegion area, RecognitionObject ro)
    {
        using var ra = area.Find(ro);
        return ra.IsExist();
    }

    /// <summary>
    /// 黑屏剧情点击。参考原版：取画面中间三分之一，纯黑比例大于 50%
    /// 且不接近全黑时点击一次，防止过场黑屏卡住。
    /// </summary>
    private bool ClickBlackGameScreen(ImageRegion area)
    {
        if ((DateTime.Now - _prevClickTime).TotalMilliseconds <= 1200)
        {
            return false;
        }

        // 只在识别到剧情界面后的 10 秒内允许黑屏点击，避免在菜单/加载界面误点
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
    /// 对话选项列表选择：先滚到顶部，再滚动到目标序号，最后按 F 确认。
    /// </summary>
    /// <param name="config">自动剧情配置</param>
    private void HandleOptionSelection(AutoSkipConfig config)
    {
        // 前台滚轮需要游戏窗口处于激活状态
        if (!SystemControl.IsGameActive())
        {
            SystemControl.ActivateWindow();
            _nextActionTime = DateTime.Now.AddMilliseconds(300);
            return;
        }

        var max = Math.Max(1, config.OptionMaxScrolls);
        var target = config.SelectLastOption
            ? max
            : config.OptionSelectIndex >= 0
                ? Math.Min(config.OptionSelectIndex, max)
                : 0;
        var step = Math.Max(80, config.OptionScrollStepMs);

        switch (_optionState)
        {
            case OptionState.Idle:
                _optionState = OptionState.ScrollTop;
                _optionSteps = 0;
                _nextActionTime = DateTime.Now.AddMilliseconds(step);
                break;

            case OptionState.ScrollTop:
                WheelAt(OptionWheelX, OptionWheelY, 1);
                _optionSteps++;
                if (_optionSteps >= max)
                {
                    _optionState = OptionState.ScrollToTarget;
                    _optionSteps = 0;
                }

                _nextActionTime = DateTime.Now.AddMilliseconds(step);
                break;

            case OptionState.ScrollToTarget:
                if (_optionSteps >= target)
                {
                    _optionState = OptionState.Confirm;
                    _nextActionTime = DateTime.Now.AddMilliseconds(step);
                    break;
                }

                WheelAt(OptionWheelX, OptionWheelY, -1);
                _optionSteps++;
                _nextActionTime = DateTime.Now.AddMilliseconds(step);
                break;

            case OptionState.Confirm:
                Press(User32.VK.VK_F);
                _optionState = OptionState.Idle;
                _optionSteps = 0;
                _nextActionTime = DateTime.Now.AddMilliseconds(800);
                break;
        }
    }

    /// <summary>
    /// 按 J 打开任务界面，选择任务分类页签，滚轮滚动到指定任务并点击，
    /// 最后按 Space 追踪、Q 返回。任务列表需要前台滚轮和点击。
    /// </summary>
    /// <param name="area">当前帧的 1080P 区域</param>
    /// <param name="config">自动剧情配置</param>
    private void HandleQuestTracking(ImageRegion area, AutoSkipConfig config)
    {
        var max = Math.Max(1, config.QuestMaxScrolls);
        var target = config.QuestSelectIndex >= 0
            ? Math.Min(config.QuestSelectIndex, max)
            : -1;
        var tabIndex = config.QuestTabIndex < 0
            ? -1
            : Math.Min(config.QuestTabIndex, QuestTabCount - 1);
        var step = Math.Max(80, config.QuestScrollStepMs);

        // 滚轮和点击需要游戏在前台
        if (!SystemControl.IsGameActive())
        {
            SystemControl.ActivateWindow();
            _nextActionTime = DateTime.Now.AddMilliseconds(400);
            return;
        }

        switch (_questState)
        {
            case QuestState.Detect:
                if (IsQuestPanelOpen(area))
                {
                    EnterQuestSelection(target, config.QuestTabIndex, step);
                }
                else
                {
                    PressForeground(User32.VK.VK_J);
                    _questState = QuestState.Opening;
                    _questAttempts = 0;
                    _nextActionTime = DateTime.Now.AddMilliseconds(800);
                    _logger.LogInformation("打开任务界面，准备选择任务");
                }

                break;

            case QuestState.Opening:
                if (IsQuestPanelOpen(area))
                {
                    _logger.LogInformation("任务界面已打开");
                    EnterQuestSelection(target, tabIndex, step);
                }
                else if (++_questAttempts < 30)
                {
                    // J 在燕云中打开的是 ESC 菜单，需要再点击“任务”菜单项进入任务界面
                    if (!_questMenuClicked && _questAttempts == 2)
                    {
                        ClickPercent(0.778, 0.692);
                        _questMenuClicked = true;
                        _logger.LogInformation("点击任务菜单");
                    }

                    // 只按一次 J，之后轮询等待界面淡入，避免再次按 J 把界面关掉
                    if (_questAttempts % 4 == 0)
                    {
                        _logger.LogInformation("等待任务界面，亮度比例 {Ratio:F2}", _lastPanelRatio);
                    }

                    _nextActionTime = DateTime.Now.AddMilliseconds(500);
                }
                else
                {
                    // 等待超时仍未识别到任务界面，保存当前画面便于诊断
                    _logger.LogWarning("未能打开任务界面，亮度比例 {Ratio:F2}，跳过自动追踪",
                        _lastPanelRatio);
                    PressForeground(User32.VK.VK_ESCAPE);
                    SaveFailureFrame(area);
                    _questState = QuestState.Done;
                    _questDone = true;
                }

                break;

            case QuestState.Tab:
                if (!_tabResetDone)
                {
                    // 先按 Q 回到最左侧的“进行中”
                    PressForeground(User32.VK.VK_Q);
                    _tabSteps++;
                    if (_tabSteps >= QuestTabResetMoves)
                    {
                        _tabResetDone = true;
                        _tabSteps = 0;
                    }

                    _nextActionTime = DateTime.Now.AddMilliseconds(step);
                    break;
                }

                if (_tabSteps >= tabIndex)
                {
                    _questState = QuestState.ScrollTop;
                    _questSteps = 0;
                    _nextActionTime = DateTime.Now.AddMilliseconds(step);
                    break;
                }

                // 按 E 向右切换到指定分类
                PressForeground(User32.VK.VK_E);
                _tabSteps++;
                _nextActionTime = DateTime.Now.AddMilliseconds(step);
                break;

            case QuestState.ScrollTop:
                WheelAt(QuestWheelX, QuestWheelY, 1);
                _questSteps++;
                if (_questSteps >= max)
                {
                    _questState = QuestState.ScrollToTarget;
                    _questSteps = 0;
                }

                _nextActionTime = DateTime.Now.AddMilliseconds(step);
                break;

            case QuestState.ScrollToTarget:
                if (_questSteps >= target)
                {
                    _questState = QuestState.Click;
                    _nextActionTime = DateTime.Now.AddMilliseconds(step);
                    break;
                }

                WheelAt(QuestWheelX, QuestWheelY, -1);
                _questSteps++;
                _nextActionTime = DateTime.Now.AddMilliseconds(step);
                break;

            case QuestState.Click:
                ClickPercent(QuestRowX, QuestRowY);
                _questState = QuestState.Track;
                _nextActionTime = DateTime.Now.AddMilliseconds(800);
                _logger.LogInformation("已点击第 {Index} 个任务", config.QuestSelectIndex);
                break;

            case QuestState.Track:
                // N = 任务追踪
                PressForeground(User32.VK.VK_N);
                _questState = QuestState.Return;
                _nextActionTime = DateTime.Now.AddMilliseconds(600);
                _logger.LogInformation("已选择任务，开始追踪");
                break;

            case QuestState.Return:
                // 任务界面中 Space = 返回任务
                PressForeground(User32.VK.VK_SPACE);
                _questState = QuestState.Close;
                _nextActionTime = DateTime.Now.AddMilliseconds(800);
                _logger.LogInformation("返回任务");
                break;

            case QuestState.Close:
                // 返回任务后任务界面通常已经关闭，只有仍然打开时才按 Q 关闭
                if (IsQuestPanelOpen(area))
                {
                    PressForeground(User32.VK.VK_Q);
                }

                _questState = QuestState.Done;
                _questDone = true;
                _nextActionTime = DateTime.Now.AddMilliseconds(500);
                _logger.LogInformation("任务追踪完成");
                break;
        }
    }

    /// <summary>
    /// 判断任务界面是否已打开。优先匹配界面文字，失败时用大面积浅灰背景兜底。
    /// </summary>
    /// <param name="area">当前帧的 1080P 区域</param>
    private bool IsQuestPanelOpen(ImageRegion area)
    {
        var assets = AutoSkipAssets.Instance;
        if (area.Find(assets.QuestPanelRo).IsExist() ||
            area.Find(assets.QuestPanelFallbackRo).IsExist())
        {
            return true;
        }

        // 任务界面左侧是连续的大面积浅灰背景，游戏世界通常较暗，用亮度比例兜底
        try
        {
            var w = area.Width;
            var h = area.Height;
            using var roi = area.DeriveCrop(new Rect(
                (int)(w * 0.08), (int)(h * 0.12),
                (int)(w * 0.55), (int)(h * 0.65)));
            using var gray = new Mat();
            Cv2.CvtColor(roi.SrcMat, gray, ColorConversionCodes.BGR2GRAY);
            using var mask = new Mat();
            Cv2.InRange(gray, new Scalar(190), new Scalar(255), mask);
            var ratio = Cv2.CountNonZero(mask) / (double)(gray.Rows * gray.Cols);
            _lastPanelRatio = ratio;
            _logger.LogDebug("任务面板亮度比例 {Ratio:F2}", ratio);
            return ratio > 0.5;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "任务面板亮度检测失败");
        }

        return false;
    }

    /// <summary>
    /// 进入任务列表选择阶段。-1 表示跟随当前选择，不滚动和点击列表。
    /// </summary>
    /// <param name="target">目标序号</param>
    /// <param name="tabIndex">任务分类页签序号</param>
    /// <param name="step">滚轮间隔</param>
    private void EnterQuestSelection(int target, int tabIndex, int step)
    {
        _questSteps = 0;
        _tabSteps = 0;
        _tabResetDone = false;
        if (target < 0)
        {
            _questState = QuestState.Track;
        }
        else if (tabIndex >= 0 && tabIndex < QuestTabCount)
        {
            _questState = QuestState.Tab;
        }
        else
        {
            _questState = QuestState.ScrollTop;
        }

        _nextActionTime = DateTime.Now.AddMilliseconds(step);
    }

    /// <summary>
    /// 在指定相对位置发送前台滚轮
    /// </summary>
    /// <param name="xPercent">相对捕获区域的 X 比例</param>
    /// <param name="yPercent">相对捕获区域的 Y 比例</param>
    /// <param name="clicks">滚动格数，正数向上，负数向下</param>
    private void WheelAt(double xPercent, double yPercent, int clicks)
    {
        MoveMouseToPercent(xPercent, yPercent);
        InputHub.Foreground.Mouse.VerticalScroll(clicks);
        _logger.LogDebug("自动剧情滚轮：{Clicks}", clicks);
    }

    /// <summary>
    /// 在指定相对位置点击鼠标左键
    /// </summary>
    /// <param name="xPercent">相对捕获区域的 X 比例</param>
    /// <param name="yPercent">相对捕获区域的 Y 比例</param>
    private void ClickPercent(double xPercent, double yPercent)
    {
        MoveMouseToPercent(xPercent, yPercent);
        InputHub.Foreground.Mouse.LeftButtonClick();
    }

    /// <summary>
    /// 把鼠标移动到捕获区域内的相对位置
    /// </summary>
    /// <param name="xPercent">X 比例</param>
    /// <param name="yPercent">Y 比例</param>
    private static void MoveMouseToPercent(double xPercent, double yPercent)
    {
        var capture = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var screen = PrimaryScreen.WorkingArea;
        var x = capture.X + capture.Width * xPercent;
        var y = capture.Y + capture.Height * yPercent;
        InputHub.Foreground.Mouse.MoveMouseTo(x * 65535d / screen.Width, y * 65535d / screen.Height);
    }

    /// <summary>
    /// 发送后台按键
    /// </summary>
    /// <param name="key">虚拟键码</param>
    private void Press(User32.VK key)
    {
        InputHub.Background.Keyboard.KeyPress(key);
        _logger.LogDebug("自动剧情按键：{Key}", key);
    }

    /// <summary>
    /// 保存任务界面识别失败时的当前帧，便于排查是按键未生效还是识别失败。
    /// </summary>
    /// <param name="area">当前帧区域</param>
    private void SaveFailureFrame(ImageRegion area)
    {
        try
        {
            var dir = Global.Absolute(@"log\autoskip");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"task-panel-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            Cv2.ImWrite(path, area.SrcMat);
            _logger.LogWarning("任务界面识别失败，截图已保存：{Path}", path);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "保存任务界面失败截图时出错");
        }
    }

    /// <summary>
    /// 发送前台按键。按住 50ms，避免游戏漏掉瞬时按键。
    /// </summary>
    /// <param name="key">虚拟键码</param>
    private void PressForeground(User32.VK key)
    {
        InputHub.Foreground.Keyboard.KeyDown(key);
        System.Threading.Thread.Sleep(50);
        InputHub.Foreground.Keyboard.KeyUp(key);
        _logger.LogDebug("自动剧情前台按键：{Key}", key);
    }
}
