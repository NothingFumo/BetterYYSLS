using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterYYSLS.Core.Config;

/// <summary>
///     示例触发器配置，用于演示实时触发器的接入方式
/// </summary>
[Serializable]
public partial class SampleTriggerConfig : ObservableObject
{
    [ObservableProperty]
    private bool _enabled = true;
}

/// <summary>
///     占位触发器配置，供接入具体游戏逻辑时使用
/// </summary>
[Serializable]
public partial class PlaceholderTriggerConfig : ObservableObject
{
    [ObservableProperty]
    private bool _enabled;
}
