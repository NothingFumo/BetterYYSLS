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
    /// 选择最后一项；关闭时按 <see cref="OptionSelectIndex"/> 选择
    /// </summary>
    [ObservableProperty]
    private bool _selectLastOption;

    /// <summary>
    /// 指定选项序号（从 0 开始，-1 表示不指定）
    /// </summary>
    [ObservableProperty]
    private int _optionSelectIndex = -1;

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
