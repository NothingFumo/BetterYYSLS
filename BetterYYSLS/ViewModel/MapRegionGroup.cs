using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 区域下拉中的一级区域及其二级子区域。
/// </summary>
public partial class MapRegionGroup : ObservableObject
{
    /// <summary>
    /// 一级区域名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 一级区域选项。
    /// </summary>
    public MapRegionOption? Region { get; init; }

    /// <summary>
    /// 二级子区域。
    /// </summary>
    public ObservableCollection<MapRegionOption> Children { get; } = [];

    /// <summary>
    /// 是否展开二级子区域。
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// 是否存在二级子区域。
    /// </summary>
    public bool HasChildren => Children.Count > 0;
}
