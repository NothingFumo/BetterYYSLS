using BetterYYSLS.Core.Godlike;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows.Media.Imaging;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 角色选择弹窗中的一项。
/// </summary>
public partial class MapRoleChoice : ObservableObject
{
    /// <summary>
    /// 创建角色选择项。
    /// </summary>
    public MapRoleChoice(GodlikeRole role)
    {
        Role = role;
    }

    /// <summary>
    /// 原始角色信息。
    /// </summary>
    public GodlikeRole Role { get; }

    /// <summary>
    /// 角色 ID。
    /// </summary>
    public string RoleId => Role.RoleId;

    /// <summary>
    /// 角色昵称。
    /// </summary>
    public string Nick => Role.Nick;

    /// <summary>
    /// 角色列表显示文本；昵称缺失时回退到角色 ID。
    /// </summary>
    public string DisplayText => Role.DisplayText;

    /// <summary>
    /// 角色服务器名称。
    /// </summary>
    public string ServerName => Role.ServerName;

    /// <summary>
    /// 角色头像。
    /// </summary>
    [ObservableProperty]
    private BitmapImage? _avatar;

    /// <summary>
    /// 当前是否被选中。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 是否由用户手动输入角色 ID 添加；只有手动角色允许移除。
    /// </summary>
    [ObservableProperty]
    private bool _isManual;

    /// <summary>
    /// 是否允许从列表移除；账号绑定角色不可移除。
    /// </summary>
    [ObservableProperty]
    private bool _canRemove;
}
