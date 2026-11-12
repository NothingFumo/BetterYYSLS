using System;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 地图工具使用的三维坐标。
/// 世界坐标使用 X/Y/Z；地图像素坐标使用 X/Y，Z 为 0。
/// </summary>
public readonly record struct MapPoint(double X, double Y, double Z = 0)
{
    /// <summary>
    /// 计算两点之间的三维欧氏距离。
    /// </summary>
    public double DistanceTo(MapPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>
    /// 计算两点之间的水平距离，忽略高度。
    /// </summary>
    public double HorizontalDistanceTo(MapPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
