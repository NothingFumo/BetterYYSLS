using BetterYYSLS.Core.Recognition;
using BetterYYSLS.GameTask.Model;

namespace BetterYYSLS.GameTask.AutoSkip.Assets;

/// <summary>
/// 自动剧情识别素材。对象与 ROI 由同目录的 Recognition.json 定义，
/// 模板以 1920×1080 截取，运行时按游戏分辨率缩放。
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
        var width = CaptureRect.Width;
        var height = CaptureRect.Height;

        ResumeRo = RecognitionAssets.Get("AutoSkip", "Resume", width, height);
        SkipRo = RecognitionAssets.Get("AutoSkip", "Skip", width, height);
        ConfirmRo = RecognitionAssets.Get("AutoSkip", "Confirm", width, height);
        PauseRo = RecognitionAssets.Get("AutoSkip", "Pause", width, height);
        FollowRo = RecognitionAssets.Get("AutoSkip", "Follow", width, height);
        OptionFirstRo = RecognitionAssets.Get("AutoSkip", "OptionFirst", width, height);
        QuestPanelRo = RecognitionAssets.Get("AutoSkip", "QuestPanel", width, height);
        QuestPanelFallbackRo = RecognitionAssets.Get("AutoSkip", "QuestPanelFallback", width, height);
    }
}
