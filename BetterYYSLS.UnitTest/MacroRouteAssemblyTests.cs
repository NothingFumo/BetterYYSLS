using BetterYYSLS.Core.Map;
using Xunit;

namespace BetterYYSLS.UnitTest;

public sealed class MacroRouteAssemblyTests
{
    [Fact]
    public void PlanTeleportClusteredRoute_StartsAtTeleportAndVisitsUnfinishedTargets()
    {
        var teleport = Marker("tp", 0, 0, isTeleport: true);
        var target = Marker("target", 2, 0);
        var snapshot = new MapSnapshot { Markers = [teleport, target] };

        var route = MacroRouteAssembler.PlanTeleportClusteredRoute(
            snapshot,
            ["target"],
            maxPoints: 10,
            allowedTeleportIds: ["tp"]);

        Assert.Equal([target], route.OrderedMarkers);
        Assert.Contains(route.Legs, leg => leg.Kind == RouteLegKind.TeleportArrival);
    }

    private static MapMarker Marker(string id, double x, double y, bool isTeleport = false)
    {
        return new MapMarker
        {
            Id = id,
            Title = id,
            LayerId = "0",
            RegionId = "map-subtype-1",
            MapSubType = "1",
            IsTeleport = isTeleport,
            WorldPosition = new MapPoint(x, y),
            MapPosition = new MapPoint(x, y)
        };
    }
}
