using System.Collections.Generic;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 宏观路线规划请求。
/// 只处理用户选中的标记，不生成可执行路径。
/// </summary>
public sealed class RouteRequest
{
    /// <summary>
    /// 可选起点。为空时使用第一个选中标记。
    /// </summary>
    public MapPoint? Start { get; set; }

    /// <summary>
    /// 选中的标记标识。
    /// </summary>
    public List<string> SelectedMarkerIds { get; set; } = [];

    /// <summary>
    /// 最大访问点数。
    /// </summary>
    public int MaxPoints { get; set; } = 64;

    /// <summary>
    /// 传送节点的额外代价。
    /// </summary>
    public double TeleportPenalty { get; set; } = 10;

    /// <summary>
    /// 步行距离的代价系数。
    /// </summary>
    public double WalkCostFactor { get; set; } = 1;

    /// <summary>
    /// 小规模行程是否使用精确动态规划求最短顺序。
    /// </summary>
    public bool UseExactOrder { get; set; }
}
