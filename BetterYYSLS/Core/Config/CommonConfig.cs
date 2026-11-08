using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterYYSLS.Core.Config;

/// <summary>
///     通用配置
/// </summary>
[Serializable]
public partial class CommonConfig : ObservableObject
{
    /// <summary>
    ///     是否启用截图保存功能
    /// </summary>
    [ObservableProperty]
    private bool _screenshotEnabled;

    /// <summary>
    ///     退出时最小化至托盘
    /// </summary>
    [ObservableProperty]
    private bool _exitToTray;
}
