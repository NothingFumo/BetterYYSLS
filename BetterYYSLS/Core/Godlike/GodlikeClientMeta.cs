namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// Godlike 从游戏进程读取坐标时使用的内存偏移。
/// </summary>
public sealed class GodlikeClientMeta
{
    /// <summary>
    /// 地图数据版本。
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// X 坐标偏移。
    /// </summary>
    public ulong OffsetX { get; set; }

    /// <summary>
    /// Y 坐标偏移。
    /// </summary>
    public ulong OffsetY { get; set; }

    /// <summary>
    /// Z 坐标偏移。
    /// </summary>
    public ulong OffsetZ { get; set; }
}
