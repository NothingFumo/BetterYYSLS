using BetterYYSLS.Core.Map;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// Godlike 网页地图使用的区域投影配置。
/// 配置来自官方实时地图脚本，世界坐标转换后为 Leaflet CRS.Simple 坐标。
/// </summary>
public sealed record GodlikeProjectionConfig(
    string SubType,
    string Name,
    string MapZone,
    string TileGroup,
    string TileUrlTemplate,
    int MapWidth,
    int MapHeight,
    double RealMeter,
    double OriginX,
    double OriginY,
    double XArrow,
    double YArrow,
    bool IsXHorizontal,
    double DefaultX,
    double DefaultY)
{
    /// <summary>
    /// 官方坐标换算中的固定比例。
    /// </summary>
    public const double CalMeter = 1024d / 1008d;

    /// <summary>
    /// 瓦片层在 CRS.Simple 下的地图范围。
    /// 主世界使用 0..256，河西使用其缩放范围，滹沱/皇宫使用 0..128。
    /// </summary>
    public double TileExtent => SubType switch
    {
        // 官方 mapInfo.bounds：主世界/河西/未央城 256，滹沱/皇宫 64。
        "1" or "2" or "5" => 256,
        _ => 64
    };

    /// <summary>
    /// 世界坐标转地图坐标。
    /// </summary>
    public MapPoint WorldToMap(double worldX, double worldY)
    {
        var scale = RealMeter * CalMeter;
        var u = ((worldX * XArrow) + OriginX) * scale;
        var v = ((worldY * YArrow) + OriginY) * scale;

        // 官方函数返回 [lat, lng]，这里统一转为平面 X/Y。
        return IsXHorizontal
            ? new MapPoint(v, u)
            : new MapPoint(u, v);
    }
}

/// <summary>
/// Godlike 燕云十六声地图投影。
/// </summary>
public static class GodlikeProjection
{
    /// <summary>
    /// 主世界瓦片模板，包含开封、河西、未央城。
    /// </summary>
    public const string MainTileUrlTemplate =
        "https://img.166.net/canonical/h72/tilemap/V20/{z}/{x}_{y}.png?imageView&v=1";

    /// <summary>
    /// 滹沱瓦片模板。
    /// </summary>
    public const string HutuoTileUrlTemplate =
        "https://img.166.net/canonical/h72/tilemap/subType3/v2/{z}/{x}_{y}.png?imageView&v=1";

    /// <summary>
    /// 皇宫瓦片模板。
    /// </summary>
    public const string PalaceTileUrlTemplate =
        "https://img.166.net/canonical/h72/tilemap/subType4/v2/{z}/{x}_{y}.png?imageView&v=1";

    private const double MainRealMeter = 256d / 24576d;
    private const double DetailRealMeter = 0.015625d;

    /// <summary>
    /// 官方五个 mapSubType 的完整配置。
    /// </summary>
    public static IReadOnlyList<GodlikeProjectionConfig> Configs { get; } =
    [
        new("1", "清河·开封·不见山", "map12", "main", MainTileUrlTemplate,
            19456, 19456, MainRealMeter, 12095.6875, 12095.6875,
            -1, 1, false, -1918.722046, -2808.292969),
        new("2", "河西", "map12", "main", MainTileUrlTemplate,
            9216, 9216, MainRealMeter, 2015.89583, 2015.8302,
            -1, 1, false, 62.84661102, 407.5228577),
        // 滹沱属于不见山子区域，但仍使用独立的 map3 瓦片，不能套用主世界 V20。
        new("3", "滹沱", "map3", "3", HutuoTileUrlTemplate,
            16384, 16384, DetailRealMeter, 4, 4036,
            -1, -1, true, -2151.075927734375, 2884.895263671875),
        new("4", "皇宫", "map4", "4", PalaceTileUrlTemplate,
            16384, 16384, DetailRealMeter, 2128, 4156,
            -1, 1, false, 300.075927734375, -2250.595263671875),
        new("5", "未央城", "map12", "main", MainTileUrlTemplate,
            24576, 24576, MainRealMeter, 12096, 12090,
            -1, 1, false, -10435.466796875, 3629.430908203125)
    ];

    /// <summary>
    /// 按 mapSubType 查找配置；未知类型回退到开封主世界。
    /// </summary>
    public static GodlikeProjectionConfig Get(string? subType)
    {
        return Configs.FirstOrDefault(config => config.SubType == subType) ?? Configs[0];
    }

    /// <summary>
    /// 世界坐标转地图坐标。
    /// </summary>
    public static MapPoint WorldToMap(string? subType, double worldX, double worldY)
    {
        return Get(subType).WorldToMap(worldX, worldY);
    }

    /// <summary>
    /// 世界坐标转地图坐标。
    /// </summary>
    public static MapPoint WorldToMap(MapPoint world, string? subType)
    {
        return WorldToMap(subType, world.X, world.Y);
    }
}
