using System;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 宏观路线绘制策略：纯传送段不绘制，投影后超远段不绘制。
/// </summary>
public static class MacroRoutePolicy
{
    /// <summary>
    /// 默认最大可绘制腿的地图投影距离。
    /// </summary>
    public const double MaxDrawableMapDistance = 3d;

    /// <summary>
    /// 判断一段宏观路线是否应绘制。
    /// </summary>
    public static bool ShouldDraw(RouteLeg leg, Func<MapPoint, MapPoint> project)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(project);
        if (leg.Kind == RouteLegKind.Teleport)
        {
            return false;
        }

        var from = project(leg.From);
        var to = project(leg.To);
        return from.HorizontalDistanceTo(to) <= MaxDrawableMapDistance;
    }
}
