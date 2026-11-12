using BetterYYSLS.Core.Map;
using Xunit;

namespace BetterYYSLS.UnitTest;

public sealed class MacroRoutePolicyTests
{
    [Fact]
    public void TeleportLegIsNeverDrawn()
    {
        var leg = new RouteLeg { Kind = RouteLegKind.Teleport };
        Assert.False(MacroRoutePolicy.ShouldDraw(leg, point => point));
    }

    [Fact]
    public void FarProjectedLegIsNotDrawn()
    {
        var leg = new RouteLeg
        {
            Kind = RouteLegKind.EstimatedWalk,
            From = new MapPoint(0, 0),
            To = new MapPoint(10, 0)
        };
        Assert.False(MacroRoutePolicy.ShouldDraw(leg, point => point));
    }

    [Fact]
    public void NearbyArrivalIsDrawn()
    {
        var leg = new RouteLeg
        {
            Kind = RouteLegKind.TeleportArrival,
            From = new MapPoint(0, 0),
            To = new MapPoint(1, 0)
        };
        Assert.True(MacroRoutePolicy.ShouldDraw(leg, point => point));
    }

    [Fact]
    public void ThreeMapUnitLegIsNotDrawn()
    {
        var leg = new RouteLeg
        {
            Kind = RouteLegKind.EstimatedWalk,
            From = new MapPoint(0, 0),
            To = new MapPoint(3.2, 0)
        };
        Assert.False(MacroRoutePolicy.ShouldDraw(leg, point => point));
    }
}
