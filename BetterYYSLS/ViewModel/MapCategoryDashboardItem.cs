using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 分类完成度面板条目。
/// </summary>
public partial class MapCategoryDashboardItem : ObservableObject
{
    /// <summary>
    /// 分类标识。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 分类名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 一级玩法名称。
    /// </summary>
    public string GroupName { get; init; } = string.Empty;

    /// <summary>
    /// 对应的分类过滤器。
    /// </summary>
    public MapCategoryFilter Filter { get; init; } = new();

    /// <summary>
    /// 点位总数。
    /// </summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// 已完成数量。
    /// </summary>
    [ObservableProperty]
    private int _completedCount;

    /// <summary>
    /// 剩余数量。
    /// </summary>
    public int RemainingCount => Math.Max(TotalCount - CompletedCount, 0);

    /// <summary>
    /// 列表显示文本。
    /// </summary>
    public string DisplayText => string.IsNullOrWhiteSpace(GroupName) || GroupName == Name
        ? $"{Name}  {CompletedCount}/{TotalCount}"
        : $"{GroupName} / {Name}  {CompletedCount}/{TotalCount}";

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnCompletedCountChanged(int value)
    {
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(DisplayText));
    }
}
