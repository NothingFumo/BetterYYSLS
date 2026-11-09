namespace BetterYYSLS.GameTask.Common;

/// <summary>
/// 当前游戏界面类型。触发器按类型过滤，避免在菜单中误按剧情键。
/// </summary>
public enum GameUiCategory
{
    /// <summary>未分类，触发器不受界面类型限制</summary>
    Unknown,

    /// <summary>主世界/普通游戏界面</summary>
    Main,

    /// <summary>剧情对话界面</summary>
    Talk,

    /// <summary>大地图界面</summary>
    BigMap,
}
