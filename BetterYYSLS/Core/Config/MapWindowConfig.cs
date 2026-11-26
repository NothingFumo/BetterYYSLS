using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace BetterYYSLS.Core.Config;

/// <summary>
/// 大地图窗口持久化设置。
/// </summary>
public partial class MapWindowConfig : ObservableObject
{
    /// <summary>
    /// 上次选择的区域名称。
    /// </summary>
    [ObservableProperty]
    private string _selectedRegionName = string.Empty;

    /// <summary>
    /// 上次选择的位面 ID。
    /// </summary>
    [ObservableProperty]
    private string _selectedLayerId = string.Empty;

    /// <summary>
    /// 有角色坐标时自动切换到角色所在地图。
    /// </summary>
    [ObservableProperty]
    private bool _autoSwitchToPlayerMap = true;

    /// <summary>
    /// 跨登录记录分类勾选与隐藏已完成。
    /// </summary>
    [ObservableProperty]
    private bool _recordFilterPreferences = true;

    /// <summary>
    /// 跨登录记录区域、子区域与位面选择。
    /// </summary>
    [ObservableProperty]
    private bool _recordMapSelection = true;

    /// <summary>
    /// 地图窗口跟随游戏窗口移动与缩放；默认关闭，保持用户拖动的位置。
    /// </summary>
    [ObservableProperty]
    private bool _followGameWindow;

    /// <summary>
    /// 地图窗口默认置顶。
    /// </summary>
    [ObservableProperty]
    private bool _alwaysOnTop = true;

    /// <summary>
    /// 隐藏小地图边缘分类图标；本轮未实现小地图，仅保留配置。
    /// </summary>
    [ObservableProperty]
    private bool _hideMinimapEdgeIcons;

    /// <summary>
    /// 记录筛选偏好时保存的可见分类 ID。
    /// </summary>
    public List<string> VisibleCategoryIds { get; set; } = [];

    /// <summary>
    /// 记录筛选偏好时保存的“隐藏已完成标点”。
    /// </summary>
    [ObservableProperty]
    private bool _hideCompleted = true;

    /// <summary>
    /// 上次选择的角色 ID；包含手动输入、不在账号绑定列表中的角色。
    /// </summary>
    [ObservableProperty]
    private string _selectedRoleId = string.Empty;

    /// <summary>
    /// 上次选择角色所属服务器编号。
    /// </summary>
    [ObservableProperty]
    private string _selectedRoleServer = string.Empty;

    /// <summary>
    /// 上次选择角色所属服务器名称。
    /// </summary>
    [ObservableProperty]
    private string _selectedRoleServerName = string.Empty;

    /// <summary>
    /// 上次选择角色昵称；接口只返回角色 ID 时为空。
    /// </summary>
    [ObservableProperty]
    private string _selectedRoleNick = string.Empty;

    /// <summary>
    /// 上次选择角色头像地址。
    /// </summary>
    [ObservableProperty]
    private string _selectedRoleIcon = string.Empty;

    /// <summary>
    /// 手动验证并加入角色列表的角色资料。
    /// </summary>
    public List<MapRoleProfile> SavedRoles { get; set; } = [];
}
