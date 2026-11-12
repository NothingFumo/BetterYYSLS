using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 官方多位面素材：一张铺在地图上的位面图片及其世界坐标范围。
/// </summary>
public sealed class MapLayerOverlay
{
    /// <summary>
    /// 所属位面 ID。
    /// </summary>
    public string LayerId { get; set; } = string.Empty;

    /// <summary>
    /// 图片使用的 mapSubType。
    /// </summary>
    public string MapSubType { get; set; } = "1";

    /// <summary>
    /// 位面图片地址。
    /// </summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// 图片范围的一个世界坐标角点。
    /// </summary>
    public MapPoint WorldA { get; set; }

    /// <summary>
    /// 图片范围的另一个世界坐标角点。
    /// </summary>
    public MapPoint WorldB { get; set; }
}

/// <summary>
/// 加载官方多位面素材并挂到对应地图图层上。
/// </summary>
public static class MapLayerOverlayCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 把官方位面图片应用到快照图层。重复调用不会重复添加。
    /// </summary>
    public static void Apply(MapSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Map", "layer-overlays.json");
        if (!File.Exists(path))
        {
            return;
        }

        IReadOnlyList<MapLayerOverlay>? overlays;
        try
        {
            overlays = JsonSerializer.Deserialize<List<MapLayerOverlay>>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch
        {
            return;
        }

        if (overlays is null)
        {
            return;
        }

        foreach (var region in snapshot.Regions)
        {
            // 主世界（1/2/5）共用 main 底图，位面素材也允许跨这三个 subtype 使用。
            var subTypes = region.SubTypes.Any(subType => subType is "1" or "2" or "5")
                ? ["1", "2", "5"]
                : region.SubTypes;
            foreach (var layer in region.Layers)
            {
                layer.Overlays.Clear();
                foreach (var overlay in overlays)
                {
                    if (overlay.LayerId != layer.Id ||
                        !subTypes.Contains(overlay.MapSubType, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    layer.Overlays.Add(overlay);
                }
            }
        }
    }
}
