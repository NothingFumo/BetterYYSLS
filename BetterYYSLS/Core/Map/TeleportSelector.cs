using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 传送节点选择：唯一拥有最近节点、同层优先、跨层回退和候选截断策略的模块。
/// </summary>
public static class TeleportSelector
{
    /// <summary>
    /// 按世界坐标选择最近传送节点；距离相同时保持原始顺序。
    /// </summary>
    public static MapMarker Nearest(MapPoint point, IReadOnlyList<MapMarker> teleports)
    {
        ArgumentNullException.ThrowIfNull(teleports);
        if (teleports.Count == 0)
        {
            throw new ArgumentException("传送节点集合不能为空。", nameof(teleports));
        }

        var best = teleports[0];
        var bestSquared = double.PositiveInfinity;
        foreach (var teleport in teleports)
        {
            var dx = point.X - teleport.WorldPosition.X;
            var dy = point.Y - teleport.WorldPosition.Y;
            var squared = (dx * dx) + (dy * dy);
            if (squared < bestSquared)
            {
                bestSquared = squared;
                best = teleport;
            }
        }

        return best;
    }

    /// <summary>
    /// 为单个标记选择访问锚点：同层节点足够近时优先同层，否则使用最近跨层节点。
    /// </summary>
    public static MapMarker SelectAccess(
        MapMarker target,
        IReadOnlyList<MapMarker> layerTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        string? layerId,
        double sameLayerFallbackDistance)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(allTeleports);
        var nearestAll = Nearest(target.WorldPosition, allTeleports);
        if (string.IsNullOrWhiteSpace(layerId) || layerTeleports.Count == 0)
        {
            return nearestAll;
        }

        var nearestLayer = Nearest(target.WorldPosition, layerTeleports);
        var layerDistance = target.WorldPosition.HorizontalDistanceTo(nearestLayer.WorldPosition);
        return layerDistance <= sameLayerFallbackDistance ? nearestLayer : nearestAll;
    }

    /// <summary>
    /// 选择起点附近的首个传送节点。
    /// </summary>
    public static MapMarker SelectInitial(
        MapPoint reference,
        IReadOnlyList<MapMarker> preferredTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        double sameLayerFallbackDistance)
    {
        var preferred = preferredTeleports;
        if (preferred.Count > 0 &&
            preferred.Min(teleport => teleport.WorldPosition.HorizontalDistanceTo(reference)) >
            sameLayerFallbackDistance)
        {
            preferred = allTeleports;
        }

        var pool = preferred.Count > 0 ? preferred : allTeleports;
        return pool
            .OrderBy(teleport => teleport.WorldPosition.HorizontalDistanceTo(reference))
            .First();
    }

    /// <summary>
    /// 为路线块挑选候选节点：同层可用时只在同层内选，大地图只保留最近若干候选。
    /// </summary>
    public static List<MapMarker> SelectCandidates(
        IReadOnlyList<MapMarker> targets,
        IReadOnlyList<MapMarker> layerTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        string? layerId,
        double sameLayerFallbackDistance,
        int maximumCandidates = 8)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(allTeleports);
        var pool = allTeleports;
        if (!string.IsNullOrWhiteSpace(layerId) && layerTeleports.Count > 0)
        {
            var nearestLayerDistance = layerTeleports.Min(teleport =>
                targets.Min(target =>
                    teleport.WorldPosition.HorizontalDistanceTo(target.WorldPosition)));
            if (nearestLayerDistance <= sameLayerFallbackDistance)
            {
                pool = layerTeleports;
            }
        }

        if (pool.Count <= 12)
        {
            return pool.ToList();
        }

        return pool
            .Select(teleport => (
                teleport,
                distance: targets.Min(target =>
                    teleport.WorldPosition.HorizontalDistanceTo(target.WorldPosition))))
            .OrderBy(item => item.distance)
            .Take(maximumCandidates)
            .Select(item => item.teleport)
            .ToList();
    }
}
