using System;

namespace BetterYYSLS.Core.Config;

/// <summary>
/// 角色选择列表中持久化的角色资料。
/// 只保存角色本身，不保存搜索框输入内容。
/// </summary>
[Serializable]
public sealed class MapRoleProfile
{
    /// <summary>
    /// 角色 ID。
    /// </summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>
    /// 服务器编号。
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// 服务器名称。
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// 角色昵称。
    /// </summary>
    public string Nick { get; set; } = string.Empty;

    /// <summary>
    /// 角色头像地址。
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 是否由用户手动输入角色 ID 添加；只有这类角色允许移除。
    /// </summary>
    public bool IsManual { get; set; }
}
