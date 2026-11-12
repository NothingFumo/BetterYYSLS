using System.Collections.Generic;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 宏观路线的一段的类型。
/// </summary>
public enum RouteLegKind
{
    /// <summary>
    /// 估算步行。
    /// </summary>
    EstimatedWalk,

    /// <summary>
    /// 传送节点之间的移动。
    /// </summary>
    Teleport,

    /// <summary>
    /// 从传送点落地后走到第一个玩法点，不绘制连线。
    /// </summary>
    TeleportArrival
}

/// <summary>
/// 宏观路线的一段。
/// </summary>
public sealed class RouteLeg
{
    /// <summary>
    /// 起点坐标。
    /// </summary>
    public MapPoint From { get; set; }

    /// <summary>
    /// 终点坐标。
    /// </summary>
    public MapPoint To { get; set; }

    /// <summary>
    /// 段类型。
    /// </summary>
    public RouteLegKind Kind { get; set; }

    /// <summary>
    /// 所属传送出发批次，用于地图上显示不同颜色。
    /// </summary>
    public int TripIndex { get; set; }

    /// <summary>
    /// 水平距离。
    /// </summary>
    public double Distance { get; set; }

    /// <summary>
    /// 估算代价。
    /// </summary>
    public double Cost { get; set; }
}

/// <summary>
/// 按访问序列生成的宏观路线。
/// 该结果只用于规划提示，不代表可执行路径。
/// </summary>
public sealed class MacroRoute
{
    /// <summary>
    /// 排序后的标记。
    /// </summary>
    public List<MapMarker> OrderedMarkers { get; set; } = [];

    /// <summary>
    /// 各段路线。
    /// </summary>
    public List<RouteLeg> Legs { get; set; } = [];

    /// <summary>
    /// 总估算代价。
    /// </summary>
    public double TotalCost { get; set; }

    /// <summary>
    /// 纯步行距离。
    /// </summary>
    public double WalkingDistance { get; set; }

    /// <summary>
    /// 段间传送次数。
    /// </summary>
    public int TeleportCount { get; set; }

    /// <summary>
    /// 使用传送节点聚类的簇数量。
    /// </summary>
    public int ClusterCount { get; set; }

    /// <summary>
    /// 是否只是估算。
    /// </summary>
    public bool IsEstimated => true;
}
