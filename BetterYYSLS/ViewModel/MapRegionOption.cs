using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 官方地图区域下拉中的一行。
/// 青州、江南等在新版客户端中存在，但当前快照没有独立点位。
/// </summary>
public sealed partial class MapRegionOption : ObservableObject
{
    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 对应的 mapSubType；为空表示当前版本没有独立数据。
    /// </summary>
    public string? SubType { get; init; }

    /// <summary>
    /// 官方区域中心世界坐标 X。
    /// </summary>
    public double WorldX { get; init; }

    /// <summary>
    /// 官方区域中心世界坐标 Y。
    /// </summary>
    public double WorldY { get; init; }

    /// <summary>
    /// 上级区域名称；为空表示一级区域。
    /// </summary>
    public string? ParentName { get; init; }

    /// <summary>
    /// 是否为官方标记的新区域。
    /// </summary>
    public bool IsNew { get; init; }

    /// <summary>
    /// 是否为当前选中区域。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}
