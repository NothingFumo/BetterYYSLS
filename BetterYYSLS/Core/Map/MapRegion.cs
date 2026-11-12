using System.Collections.Generic;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 地图中的独立地理区域。
/// 每个区域拥有自己的图层与坐标标定。
/// </summary>
public sealed class MapRegion
{
    /// <summary>
    /// 区域标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属地图。
    /// </summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 区域对应的官方 mapSubType。
    /// </summary>
    public List<string> SubTypes { get; set; } = [];

    /// <summary>
    /// 官方 mapZone，例如 map12、map3、map4。
    /// </summary>
    public string? MapZone { get; set; }

    /// <summary>
    /// 瓦片分组。
    /// </summary>
    public string? TileGroup { get; set; }

    /// <summary>
    /// 瓦片层在 CRS.Simple 下的范围。
    /// </summary>
    public double TileExtent { get; set; } = 256;

    /// <summary>
    /// 区域图层。
    /// </summary>
    public List<MapLayer> Layers { get; set; } = [];

    /// <summary>
    /// 地图底图瓦片地址模板，例如
    /// https://img.166.net/canonical/h72/tilemap/V20/{z}/{x}_{y}.png?imageView&amp;v=1。
    /// </summary>
    public string? TileUrlTemplate { get; set; }

    /// <summary>
    /// 瓦片最小层级。
    /// </summary>
    public int TileMinZoom { get; set; } = 3;

    /// <summary>
    /// 瓦片最大层级。
    /// </summary>
    public int TileMaxZoom { get; set; } = 8;
}
