using System.Collections.Generic;
using System.Linq;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 地图标记的官方详情。
/// </summary>
public sealed class MapMarkerDetails
{
    /// <summary>
    /// 标点标识。
    /// </summary>
    public string PointId { get; set; } = string.Empty;

    /// <summary>
    /// 名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 所属城市。
    /// </summary>
    public string? CityName { get; set; }

    /// <summary>
    /// 图标地址。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 描述图片地址。
    /// </summary>
    public string? DescriptionIcon { get; set; }

    /// <summary>
    /// 最后所属分类名称。
    /// </summary>
    public string? LastSeriesName { get; set; }

    /// <summary>
    /// 官方 mapSubType。
    /// </summary>
    public string? MapSubType { get; set; }

    /// <summary>
    /// 是否收藏。
    /// </summary>
    public bool Favorite { get; set; }

    /// <summary>
    /// 点位访问标记。
    /// </summary>
    public bool Visit { get; set; }

    /// <summary>
    /// 当前角色访问标记；没有角色上下文时为 null。
    /// </summary>
    public bool? RoleVisit { get; set; }

    /// <summary>
    /// 点位来源，例如 LOG、MANUAL。
    /// </summary>
    public string? PointSourceType { get; set; }

    /// <summary>
    /// 官方区域 ID。
    /// </summary>
    public string? RegionId { get; set; }

    /// <summary>
    /// 连续贡献者数量。
    /// </summary>
    public int TotalCommitterNum { get; set; }

    /// <summary>
    /// 官方完成状态：visit || roleVisit。
    /// </summary>
    public bool IsCompleted => Visit || RoleVisit == true;

    /// <summary>
    /// 完成状态文本。
    /// </summary>
    public string CompletionText => IsCompleted ? "已完成" : "未完成";

    /// <summary>
    /// 访问状态文本。
    /// </summary>
    public string VisitText => $"visit={Visit} · roleVisit={(RoleVisit.HasValue ? RoleVisit.Value.ToString() : "null")}";

    /// <summary>
    /// 贡献者名称。
    /// </summary>
    public List<string> Committers { get; set; } = [];

    /// <summary>
    /// 详情摘要。
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(CityName))
            {
                parts.Add(CityName);
            }

            if (!string.IsNullOrWhiteSpace(LastSeriesName))
            {
                parts.Add(LastSeriesName);
            }

            if (Committers.Count > 0)
            {
                parts.Add($"贡献者 {string.Join("、", Committers.Take(3))}");
            }

            return string.Join(" · ", parts);
        }
    }
}
