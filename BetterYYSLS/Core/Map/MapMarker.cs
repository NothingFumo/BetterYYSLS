using System.Collections.Generic;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 地图上的兴趣点。
/// 世界坐标用于路线计算，地图像素坐标用于渲染。
/// </summary>
public sealed class MapMarker
{
    /// <summary>
    /// 标记唯一标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属地图。
    /// </summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>
    /// 所属区域。
    /// </summary>
    public string RegionId { get; set; } = string.Empty;

    /// <summary>
    /// 所属图层。
    /// </summary>
    public string? LayerId { get; set; }

    /// <summary>
    /// 官方 mapSubType。
    /// </summary>
    public string? MapSubType { get; set; }

    /// <summary>
    /// 主分类。
    /// </summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// 标记所属的全部分类。
    /// Godlike 的一个标记可以同时属于多个 series。
    /// </summary>
    public List<string> CategoryIds { get; set; } = [];

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 可选描述。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 标记图标地址。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 世界坐标。
    /// </summary>
    public MapPoint WorldPosition { get; set; }

    /// <summary>
    /// 地图像素坐标。
    /// </summary>
    public MapPoint MapPosition { get; set; }

    /// <summary>
    /// 是否为传送节点。
    /// </summary>
    public bool IsTeleport { get; set; }

    /// <summary>
    /// 是否为用户自定义标记。
    /// </summary>
    public bool IsCustom { get; set; }
}
