using BetterYYSLS.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterYYSLS.Core.Config;

/// <summary>
///     快捷键配置。格式必须是“快捷键 + 快捷键类型”。
/// </summary>
[Serializable]
public partial class HotKeyConfig : ObservableObject
{
    /// <summary>
    ///     启动/停止实时触发器
    /// </summary>
    [ObservableProperty]
    private string _startStopHotkey = "F11";

    [ObservableProperty]
    private string _startStopHotkeyType = HotKeyTypeEnum.GlobalRegister.ToString();

    /// <summary>
    ///     保存当前画面截图
    /// </summary>
    [ObservableProperty]
    private string _takeScreenshotHotkey = "";

    [ObservableProperty]
    private string _takeScreenshotHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();
}
