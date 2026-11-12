using BetterYYSLS.Core.Map;
using Xunit;

namespace BetterYYSLS.UnitTest;

public sealed class TeleportSelectorTests
{
    [Fact]
    public void SelectAccess_PrefersNearbySameLayer()
    {
        var target = Marker("target", "0", 10, 10);
        var sameLayer = Marker("same", "1", 12, 10);
        var otherLayer = Marker("other", "2", 2, 2);

        var selected = TeleportSelector.SelectAccess(
            target,
            [sameLayer],
            [sameLayer, otherLayer],
            "0",
            sameLayerFallbackDistance: 5);

        Assert.Equal(sameLayer, selected);
    }

    [Fact]
    public void SelectAccess_FallsBackWhenSameLayerIsFar()
    {
        var target = Marker("target", "0", 10, 10);
        var sameLayer = Marker("same", "1", 100, 100);
        var otherLayer = Marker("other", "2", 12, 10);

        var selected = TeleportSelector.SelectAccess(
            target,
            [sameLayer],
            [sameLayer, otherLayer],
            "0",
            sameLayerFallbackDistance: 5);

        Assert.Equal(otherLayer, selected);
    }

    [Fact]
    public void SelectInitial_UsesPreferredWhenWithinFallback()
    {
        var preferred = Marker("preferred", "0", 1, 0);
        var other = Marker("other", "1", 2, 0);

        var selected = TeleportSelector.SelectInitial(
            new MapPoint(0, 0),
            [preferred],
            [preferred, other],
            sameLayerFallbackDistance: 5);

        Assert.Equal(preferred, selected);
    }

    [Fact]
    public void SelectCandidates_TruncatesToNearestCandidates()
    {
        var targets = new[] { Marker("target", "0", 0, 0) };
        var teleports = Enumerable.Range(1, 20)
            .Select(index => Marker($"tp-{index}", "0", index, 0))
            .ToList();

        var candidates = TeleportSelector.SelectCandidates(
            targets,
            teleports,
            teleports,
            "0",
            sameLayerFallbackDistance: 100);

        Assert.Equal(8, candidates.Count);
        Assert.Equal("tp-1", candidates[0].Id);
    }

    private static MapMarker Marker(string id, string layer, double x, double y)
    {
        return new MapMarker
        {
            Id = id,
            LayerId = layer,
            WorldPosition = new MapPoint(x, y),
            MapPosition = new MapPoint(x, y)
        };
    }
}
