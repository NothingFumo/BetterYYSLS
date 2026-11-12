using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 官方一级玩法分类及其子分类。
/// </summary>
public partial class MapCategoryGroup : ObservableObject
{
    /// <summary>
    /// 一级分类标识。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 一级分类名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 子分类。
    /// </summary>
    public List<MapCategoryFilter> Categories { get; init; } = [];
}
