using BetterYYSLS.Core.Recognition;
using BetterYYSLS.GameTask.Model;
using OpenCvSharp;

namespace BetterYYSLS.GameTask.AutoSkip.Assets;

/// <summary>
/// 自动剧情识别素材。模板以 1920×1080 截取，运行时按游戏分辨率自动缩放。
/// 素材来源为《燕云十六声》剧情界面截图，ROI 按当前捕获区域比例计算。
/// </summary>
public class AutoSkipAssets : BaseAssets<AutoSkipAssets>
{
    /// <summary>“T 继续”提示</summary>
    public RecognitionObject ResumeRo { get; }

    /// <summary>“Space 跳过”提示</summary>
    public RecognitionObject SkipRo { get; }

    /// <summary>“Space 确认”跳过确认弹窗</summary>
    public RecognitionObject ConfirmRo { get; }

    /// <summary>“T 暂停”提示，表示剧情正在播放</summary>
    public RecognitionObject PauseRo { get; }

    /// <summary>“G 跟随”提示</summary>
    public RecognitionObject FollowRo { get; }

    /// <summary>对话选项第一项</summary>
    public RecognitionObject OptionFirstRo { get; }

    /// <summary>任务界面“进行中”标签，用于判断任务面板是否打开</summary>
    public RecognitionObject QuestPanelRo { get; }

    /// <summary>任务界面左下角“返回”，作为面板识别的兜底</summary>
    public RecognitionObject QuestPanelFallbackRo { get; }

    private AutoSkipAssets()
    {
        var w = CaptureRect.Width;
        var h = CaptureRect.Height;

        // 右下角操作提示区
        ResumeRo = Build("resume.png", new Rect(w - 340, h - 110, 180, 90), 0.8);
        SkipRo = Build("skip.png", new Rect(w - 220, h - 110, 210, 90), 0.8);
        PauseRo = Build("pause.png", new Rect(w - 380, h - 110, 210, 90), 0.8);

        // 屏幕中央的跳过确认
        ConfirmRo = Build("confirm.png", new Rect(w / 2 - 240, h / 2 + 30, 480, 130), 0.8);

        // NPC 头顶的跟随提示
        FollowRo = Build("follow.png", new Rect(w / 2 - 320, h / 2 + 20, 400, 160), 0.72);

        // 右侧对话选项列表
        OptionFirstRo = Build("option_first.png", new Rect(w - 760, h / 2 + 40, 760, 200), 0.75);

        // 任务界面左上角“进行中”标签
        QuestPanelRo = Build("quest_panel.png",
            new Rect((int)(w * 0.05), (int)(h * 0.02), (int)(w * 0.16), (int)(h * 0.1)), 0.8);

        // 任务界面左下角“返回”，标签颜色变化时仍可识别
        QuestPanelFallbackRo = Build("quest_panel_fallback.png",
            new Rect((int)(w * 0.02), (int)(h * 0.86), (int)(w * 0.15), (int)(h * 0.12)), 0.8);
    }

    /// <summary>
    /// 构建单个识别对象
    /// </summary>
    /// <param name="file">模板文件名</param>
    /// <param name="roi">识别区域（当前捕获区域坐标）</param>
    /// <param name="threshold">匹配阈值</param>
    private static RecognitionObject Build(string file, Rect roi, double threshold)
    {
        return new RecognitionObject
        {
            Name = file,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoSkip", file),
            RegionOfInterest = roi,
            Threshold = threshold,
            DrawOnWindow = true,
        }.InitTemplate();
    }
}
