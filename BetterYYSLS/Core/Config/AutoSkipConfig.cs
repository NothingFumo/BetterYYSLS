using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterYYSLS.Core.Config;

/// <summary>
/// 自动剧情配置。对应《燕云十六声》的剧情界面按键。
/// </summary>
[Serializable]
public partial class AutoSkipConfig : ObservableObject
{
    /// <summary>
    /// 是否启用自动剧情
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>
    /// 识别到“Space 跳过”时自动跳过本段剧情
    /// </summary>
    [ObservableProperty]
    private bool _skipCutscene = true;

    /// <summary>
    /// 识别到“T 继续”时自动继续
    /// </summary>
    [ObservableProperty]
    private bool _resumeWhenPaused = true;

    /// <summary>
    /// 识别到“T 暂停”时先按 R 倍速播放
    /// </summary>
    [ObservableProperty]
    private bool _speedUpDialogue = true;

    /// <summary>
    /// 识别到“G 跟随”时自动跟随 NPC
    /// </summary>
    [ObservableProperty]
    private bool _followNpc = true;

    /// <summary>
    /// 识别到对话选项时自动选择
    /// </summary>
    [ObservableProperty]
    private bool _selectFirstOption = true;

    /// <summary>
    /// 选择选项前通过游戏进程音频的 Silero VAD 等待语音结束。
    /// </summary>
    [ObservableProperty]
    private bool _autoWaitDialogueOptionVoiceEnabled = false;

    /// <summary>
    /// 语音检测最长等待秒数。
    /// </summary>
    [ObservableProperty]
    private int _dialogueOptionVoiceMaxWaitSeconds = 30;

    /// <summary>
    /// 选项等待结束后的额外延迟（毫秒），也作为 VAD 不可用时的回退延迟。
    /// </summary>
    [ObservableProperty]
    private int _afterChooseOptionSleepDelay = 0;

    /// <summary>
    /// 选择最后一项；关闭时按 <see cref="OptionSelectIndex"/> 选择
    /// </summary>
    [ObservableProperty]
    private bool _selectLastOption;

    /// <summary>
    /// 随机选择已有选项。
    /// </summary>
    [ObservableProperty]
    private bool _selectRandomOption;

    /// <summary>
    /// 游戏失焦时使用后台输入继续剧情。
    /// </summary>
    [ObservableProperty]
    private bool _runBackgroundEnabled = true;

    /// <summary>
    /// 后台剧情结束后自动切回游戏前台。
    /// </summary>
    [ObservableProperty]
    private bool _bringGameToFrontAfterBackgroundDialogEnabled = true;

    /// <summary>
    /// 指定选项序号（从 0 开始，-1 表示不指定）
    /// </summary>
    [ObservableProperty]
    private int _optionSelectIndex = -1;

    /// <summary>
    /// 优先选择的选项关键词，换行、逗号或分号分隔；命中后优先点击。
    /// </summary>
    [ObservableProperty]
    private string _optionPriorityKeywords = string.Empty;

    /// <summary>
    /// 不自动选择的选项关键词，换行、逗号或分号分隔。
    /// </summary>
    [ObservableProperty]
    private string _optionPauseKeywords = string.Empty;

    /// <summary>
    /// 列表最多滚动格数，用于滚到顶部或底部
    /// </summary>
    [ObservableProperty]
    private int _optionMaxScrolls = 8;

    /// <summary>
    /// 滚轮每一步的间隔（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _optionScrollStepMs = 150;

    /// <summary>
    /// 启用自动剧情时先按 J 打开任务界面，按 Space 追踪任务，再按 Q 返回
    /// </summary>
    [ObservableProperty]
    private bool _trackQuestOnStart = true;

    /// <summary>
    /// 任务页签序号：0 进行中、1 主章、2 残章、3 奇遇、4 探游；-1 表示不切换
    /// </summary>
    [ObservableProperty]
    private int _questTabIndex = 0;

    /// <summary>
    /// 指定任务在列表中的序号（从 0 开始，-1 表示跟随当前选择）
    /// </summary>
    [ObservableProperty]
    private int _questSelectIndex = -1;

    /// <summary>
    /// 任务列表最多滚动格数，用于滚到顶部
    /// </summary>
    [ObservableProperty]
    private int _questMaxScrolls = 10;

    /// <summary>
    /// 任务列表滚轮每一步的间隔（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _questScrollStepMs = 150;
}
