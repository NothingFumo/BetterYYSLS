using System.Collections.Generic;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 区域内的空间层，例如地面、洞穴或楼层。
/// </summary>
public sealed class MapLayer
{
    /// <summary>
    /// 图层标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 图层显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 楼层序号，用于排序和分层显示。
    /// </summary>
    public int Floor { get; set; }

    /// <summary>
    /// 位面图标资源。
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// 选中态位面图标资源。
    /// </summary>
    public string? ActiveIconUrl { get; set; }

    /// <summary>
    /// 官方多位面覆盖图片。
    /// </summary>
    public List<MapLayerOverlay> Overlays { get; set; } = [];

    /// <summary>
    /// 是否只显示官方图标，不显示额外文字（河西前后位面）。
    /// </summary>
    public bool IconOnly { get; set; }

    /// <summary>
    /// 是否默认显示。
    /// </summary>
    public bool IsVisibleByDefault { get; set; } = true;
}
