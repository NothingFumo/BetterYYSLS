using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Point = System.Windows.Point;

namespace BetterYYSLS.Core.Config;

/// <summary>
///     遮罩窗口配置
/// </summary>
[Serializable]
public partial class MaskWindowConfig : ObservableObject
{
    /// <summary>
    ///     控件是否锁定（禁止拖拽移动）
    /// </summary>
    [ObservableProperty]
    private bool _controlLocked = true;

    /// <summary>
    ///     是否在遮罩窗口上显示识别结果
    /// </summary>
    [ObservableProperty]
    private bool _displayRecognitionResultsOnMask = true;

    /// <summary>
    ///     日志窗口位置与大小
    /// </summary>
    [ObservableProperty]
    private Point _logBoxLocation;

    /// <summary>
    ///     是否启用遮罩窗口
    /// </summary>
    [ObservableProperty]
    private bool _maskEnabled = true;

    /// <summary>
    ///     显示日志窗口
    /// </summary>
    [ObservableProperty]
    private bool _showLogBox = true;

    /// <summary>
    ///     显示状态指示
    /// </summary>
    [ObservableProperty]
    private bool _showStatus = true;
}
