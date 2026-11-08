using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterYYSLS.Core.Config;

/// <summary>
///     游戏启动配置。框架不绑定具体游戏，进程名和安装路径由用户填写。
/// </summary>
[Serializable]
public partial class GameStartConfig : ObservableObject
{
    /// <summary>
    ///     目标游戏进程名（不含 .exe）。为空时只能手动选择窗口。
    /// </summary>
    [ObservableProperty]
    private string _processName = "";

    /// <summary>
    ///     游戏启动参数
    /// </summary>
    [ObservableProperty]
    private string _gameStartArgs = "";

    /// <summary>
    ///     游戏安装路径（可执行文件）
    /// </summary>
    [ObservableProperty]
    private string _installPath = "";

    /// <summary>
    ///     找不到游戏窗口时，是否按安装路径联动启动
    /// </summary>
    [ObservableProperty]
    private bool _linkedStartEnabled;
}
