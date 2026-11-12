namespace BetterYYSLS.Core.Map;

/// <summary>
/// 地图标记分类。
/// Godlike 的分类只通过此类映射为内部稳定标识。
/// </summary>
public sealed class MapCategory
{
    /// <summary>
    /// 内部稳定标识，不随显示名称变化。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 界面上显示的名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 分类图标资源键。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 父分类标识；顶级分类为空。
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// 分类显示顺序，数值越小越靠前。
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 是否默认显示。
    /// </summary>
    public bool IsVisibleByDefault { get; set; } = true;
}
