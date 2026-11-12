using System;
using System.Collections.Generic;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 角色作用域的标记完成状态。地图快照只读，完成状态不写回标记。
/// </summary>
public sealed class MarkerCompletionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, MarkerCompletion>> _byRole =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 读取指定角色的完成状态；未知时返回未完成且未知。
    /// </summary>
    public MarkerCompletion Get(string? roleId, string markerId)
    {
        if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(markerId))
        {
            return default;
        }

        lock (_gate)
        {
            return _byRole.TryGetValue(roleId, out var markers) &&
                   markers.TryGetValue(markerId, out var completion)
                ? completion
                : default;
        }
    }

    /// <summary>
    /// 写入指定角色的完成状态。
    /// </summary>
    public void Set(string? roleId, string markerId, MarkerCompletion completion)
    {
        if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(markerId))
        {
            return;
        }

        lock (_gate)
        {
            if (!_byRole.TryGetValue(roleId, out var markers))
            {
                markers = new Dictionary<string, MarkerCompletion>(StringComparer.Ordinal);
                _byRole[roleId] = markers;
            }

            markers[markerId] = completion;
        }
    }

    /// <summary>
    /// 清除某个角色的完成状态。
    /// </summary>
    public void Clear(string? roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return;
        }

        lock (_gate)
        {
            _byRole.Remove(roleId);
        }
    }
}
