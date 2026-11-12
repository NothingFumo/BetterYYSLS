using System.Collections.Generic;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 某一地图版本下的区域、分类和标记集合。
/// 快照可离线浏览，在线数据只负责更新快照。
/// </summary>
public sealed class MapSnapshot
{
    /// <summary>
    /// 地图标识。
    /// </summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>
    /// 地图名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 地图版本。
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 地图区域。
    /// </summary>
    public List<MapRegion> Regions { get; set; } = [];

    /// <summary>
    /// 标记分类。
    /// </summary>
    public List<MapCategory> Categories { get; set; } = [];

    /// <summary>
    /// 地图标记。
    /// </summary>
    public List<MapMarker> Markers { get; set; } = [];
}
