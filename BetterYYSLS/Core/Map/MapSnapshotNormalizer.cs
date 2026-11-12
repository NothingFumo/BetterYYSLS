using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 地图快照 normalization：唯一拥有标记 identity 去重与基础字段清理的模块。
/// </summary>
public static class MapSnapshotNormalizer
{
    /// <summary>
    /// 清理重复标记，返回规范化后的快照。
    /// </summary>
    public static MapSnapshot Normalize(MapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Markers = snapshot.Markers
            .Where(marker => !string.IsNullOrWhiteSpace(marker.Id))
            .GroupBy(marker => marker.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        return snapshot;
    }
}
