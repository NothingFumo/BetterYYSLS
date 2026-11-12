using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media.Imaging;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 地图分类的显示开关。
/// </summary>
public partial class MapCategoryFilter : ObservableObject
{
    /// <summary>
    /// 分类标识。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 父分类标识。
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 官方分类图标地址。
    /// </summary>
    public string? IconUrl { get; init; }

    /// <summary>
    /// 是否显示该分类。
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// 已下载的分类图标。
    /// </summary>
    [ObservableProperty]
    private BitmapImage? _iconImage;
}
