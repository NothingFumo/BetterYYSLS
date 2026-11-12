using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 区域锚点：用于按世界坐标判断标记属于哪个一级/子区域。
/// </summary>
public sealed record MarkerRegionAnchor(
    string Name,
    string? ParentName,
    double WorldX,
    double WorldY);

/// <summary>
/// 标记完成状态查询结果。
/// </summary>
public readonly record struct MarkerCompletion(bool IsKnown, bool IsCompleted);

/// <summary>
/// 标记范围查询请求。
/// 调用方只提供当前界面选择，不在这里拼装筛选谓词。
/// </summary>
public sealed record MarkerScopeRequest
{
    /// <summary>
    /// 全部标记。
    /// </summary>
    public required IReadOnlyList<MapMarker> Markers { get; init; }

    /// <summary>
    /// 当前选中的区域；为空时按 RegionId 过滤。
    /// </summary>
    public string? SelectedRegionName { get; init; }

    /// <summary>
    /// 当前选中区域的上级名称。
    /// </summary>
    public string? SelectedRegionParentName { get; init; }

    /// <summary>
    /// 当前选中区域的官方 mapSubType。
    /// </summary>
    public string? SelectedRegionSubType { get; init; }

    /// <summary>
    /// 没有官方区域选项时使用的 RegionId。
    /// </summary>
    public string? SelectedRegionId { get; init; }

    /// <summary>
    /// 同区域层级的锚点集合。
    /// </summary>
    public IReadOnlyList<MarkerRegionAnchor> RegionAnchors { get; init; } = [];

    /// <summary>
    /// 当前位面。
    /// </summary>
    public string? SelectedLayerId { get; init; }

    /// <summary>
    /// 当前勾选的标记分类。
    /// </summary>
    public IReadOnlySet<string> VisibleCategoryIds { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 仪表盘选中的分类；为空时使用 VisibleCategoryIds。
    /// </summary>
    public string? SelectedCategoryId { get; init; }

    /// <summary>
    /// 快照中的标记分类，用于计算分类闭包。
    /// </summary>
    public IReadOnlyList<MapCategory> Categories { get; init; } = [];

    /// <summary>
    /// 搜索文本。
    /// </summary>
    public string SearchText { get; init; } = string.Empty;

    /// <summary>
    /// 是否隐藏已完成标记。
    /// </summary>
    public bool HideCompleted { get; init; }

    /// <summary>
    /// 完成状态未知时是否允许显示。
    /// </summary>
    public bool ShowUnknownCompletion { get; init; }

    /// <summary>
    /// 完成状态查询。
    /// </summary>
    public Func<MapMarker, MarkerCompletion> CompletionOf { get; init; } = _ => default;

}

/// <summary>
/// 标记范围：唯一拥有区域、位面、分类、搜索、完成状态和锚点参与的模块。
/// </summary>
public static class MarkerScope
{
    /// <summary>
    /// 当前地图上可见的标记。传送节点始终保留。
    /// </summary>
    public static List<MapMarker> Visible(MarkerScopeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var categoryIds = ResolveSelectedCategoryIds(request);
        var keyword = request.SearchText.Trim();
        return request.Markers
            .Where(marker => MatchesRegion(marker, request))
            .Where(marker => marker.IsTeleport ||
                             categoryIds is null ||
                             MatchesCategory(marker, categoryIds))
            .Where(marker => marker.IsTeleport || !request.HideCompleted ||
                             MatchesCompletion(marker, request))
            .Where(marker => MatchesLayer(marker, request))
            .Where(marker => marker.IsTeleport ||
                             (categoryIds is null && request.VisibleCategoryIds.Count > 0 &&
                              MatchesVisibleCategory(marker, request)) ||
                             categoryIds is not null)
            .Where(marker => marker.IsTeleport ||
                             keyword.Length == 0 ||
                             marker.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                             (marker.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    /// <summary>
    /// 需要读取完成状态的标记：不应用“隐藏已完成”，但仍应用区域、位面、分类和搜索。
    /// </summary>
    public static List<MapMarker> CompletionCandidates(MarkerScopeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var categoryIds = ResolveSelectedCategoryIds(request);
        var keyword = request.SearchText.Trim();
        return request.Markers
            .Where(marker => MatchesRegion(marker, request))
            .Where(marker => MatchesLayer(marker, request))
            .Where(marker => categoryIds is not null
                ? MatchesCategory(marker, categoryIds)
                : request.VisibleCategoryIds.Count > 0 && MatchesVisibleCategory(marker, request))
            .Where(marker => keyword.Length == 0 ||
                             marker.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                             (marker.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(marker => !marker.Id.StartsWith("anchor:", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// 进入宏观路线的标记：未完成、非传送、非锚点，且满足路线锚点距离。
    /// </summary>
    public static List<MapMarker> RouteTargets(MarkerScopeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Visible(request)
            .Where(marker => !marker.IsTeleport)
            .Where(marker => !IsCompleted(marker, request))
            .Where(marker => !marker.Id.StartsWith("anchor:", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// 判断标记是否属于当前选中区域。
    /// </summary>
    public static bool InSelectedRegion(MapMarker marker, MarkerScopeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MatchesRegion(marker, request);
    }

    /// <summary>
    /// 计算标记分类闭包。
    /// </summary>
    public static HashSet<string> GetCategoryIds(IReadOnlyList<MapCategory> categories, string categoryId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { categoryId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var category in categories)
            {
                if (category.ParentId is { } parentId &&
                    ids.Contains(parentId) &&
                    ids.Add(category.Id))
                {
                    changed = true;
                }
            }
        }

        return ids;
    }

    private static IReadOnlySet<string>? ResolveSelectedCategoryIds(MarkerScopeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SelectedCategoryId))
        {
            return null;
        }

        return GetCategoryIds(request.Categories, request.SelectedCategoryId);
    }

    private static bool MatchesRegion(MapMarker marker, MarkerScopeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SelectedRegionSubType))
        {
            return string.IsNullOrWhiteSpace(request.SelectedRegionId) ||
                   marker.RegionId == request.SelectedRegionId;
        }

        if (request.SelectedRegionSubType == "5")
        {
            if (marker.MapSubType is not ("1" or "5"))
            {
                return false;
            }
        }
        else if (marker.MapSubType != request.SelectedRegionSubType)
        {
            return false;
        }

        var candidates = request.SelectedRegionParentName is null
            ? request.RegionAnchors.Where(anchor => anchor.ParentName is null).ToList()
            : request.RegionAnchors.Where(anchor => anchor.ParentName == request.SelectedRegionParentName).ToList();
        if (candidates.Count <= 1)
        {
            return true;
        }

        var nearest = candidates
            .OrderBy(anchor =>
                ((anchor.WorldX - marker.WorldPosition.X) * (anchor.WorldX - marker.WorldPosition.X)) +
                ((anchor.WorldY - marker.WorldPosition.Y) * (anchor.WorldY - marker.WorldPosition.Y)))
            .First();
        if (nearest.Name == request.SelectedRegionName)
        {
            return true;
        }

        return request.SelectedRegionName == "开封" && nearest.Name == "皇宫";
    }

    private static bool MatchesLayer(MapMarker marker, MarkerScopeRequest request)
    {
        return string.IsNullOrWhiteSpace(request.SelectedLayerId) ||
               string.IsNullOrWhiteSpace(marker.LayerId) ||
               marker.LayerId == request.SelectedLayerId;
    }

    private static bool MatchesCategory(MapMarker marker, IReadOnlySet<string> categoryIds)
    {
        return (marker.CategoryId is { } primary && categoryIds.Contains(primary)) ||
               marker.CategoryIds.Any(categoryIds.Contains);
    }

    private static bool MatchesVisibleCategory(MapMarker marker, MarkerScopeRequest request)
    {
        return request.VisibleCategoryIds.Contains(marker.CategoryId) ||
               marker.CategoryIds.Any(request.VisibleCategoryIds.Contains);
    }

    private static bool MatchesCompletion(MapMarker marker, MarkerScopeRequest request)
    {
        var completion = request.CompletionOf(marker);
        if (!completion.IsKnown)
        {
            return request.ShowUnknownCompletion;
        }

        return !completion.IsCompleted;
    }

    private static bool IsCompleted(MapMarker marker, MarkerScopeRequest request)
    {
        var completion = request.CompletionOf(marker);
        return completion.IsKnown && completion.IsCompleted;
    }
}
