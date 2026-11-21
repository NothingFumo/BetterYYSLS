using BetterYYSLS.Core.Godlike;
using BetterYYSLS.Core.Map;
using Xunit;

namespace BetterYYSLS.UnitTest;

/// <summary>
/// 官方坐标切换的坐标归一化测试。
/// </summary>
public sealed class GodlikeProjectionTests
{
    /// <summary>
    /// 河西旧坐标范围无条件施加官方偏移，不依赖当前选中的区域。
    /// </summary>
    [Fact]
    public void NormalizeWorldLocation_HexiLegacyFrame_AppliesOffset()
    {
        var normalized = GodlikeProjection.NormalizeWorldLocation(-6000, 7000);

        Assert.Equal(-2572.6, normalized.X, 3);
        Assert.Equal(3218.5, normalized.Y, 3);
    }

    /// <summary>
    /// 旧坐标范围之外保持原始世界坐标。
    /// </summary>
    [Fact]
    public void NormalizeWorldLocation_OutsideLegacyFrame_KeepsLocation()
    {
        var location = new MapPoint(-2151.07592773437, 2884.89526367187);

        var normalized = GodlikeProjection.NormalizeWorldLocation(location.X, location.Y);

        Assert.Equal(location.X, normalized.X);
        Assert.Equal(location.Y, normalized.Y);
    }
}
