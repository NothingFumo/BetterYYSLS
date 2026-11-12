using BetterYYSLS.Core.Map;
using Xunit;

namespace BetterYYSLS.UnitTest;

public sealed class MarkerScopeTests
{
    [Fact]
    public void GetCategoryIds_IncludesDescendants()
    {
        var categories = new List<MapCategory>
        {
            new() { Id = "root" },
            new() { Id = "child", ParentId = "root" },
            new() { Id = "leaf", ParentId = "child" }
        };

        var ids = MarkerScope.GetCategoryIds(categories, "root");

        Assert.Equal(new[] { "child", "leaf", "root" }, ids.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Visible_KeepsTeleportsWithoutSelectedCategories()
    {
        var teleport = Marker("teleport", "2", layer: "1");
        teleport.IsTeleport = true;
        var chest = Marker("chest", "9", layer: "1");

        var markers = MarkerScope.Visible(Request(teleport, chest));

        Assert.Equal(new[] { teleport }, markers);
    }

    [Fact]
    public void Visible_HidesKnownCompletedButHonorsUnknownPolicy()
    {
        var knownCompleted = Marker("completed", "9", layer: "0");
        var knownIncomplete = Marker("incomplete", "9", layer: "0");
        var unknown = Marker("unknown", "9", layer: "0");
        var request = Request(knownCompleted, knownIncomplete, unknown) with
        {
            VisibleCategoryIds = new HashSet<string> { "9" },
            HideCompleted = true,
            ShowUnknownCompletion = false,
            CompletionOf = marker => marker.Id switch
            {
                "completed" => new MarkerCompletion(true, true),
                "incomplete" => new MarkerCompletion(true, false),
                _ => default
            }
        };
        Assert.Equal(new[] { knownIncomplete }, MarkerScope.Visible(request));

        var withUnknown = request with { ShowUnknownCompletion = true };
        Assert.Equal(new[] { knownIncomplete, unknown }, MarkerScope.Visible(withUnknown));
    }

    [Fact]
    public void Visible_AppliesLayerAndCategoryClosure()
    {
        var parentMarker = Marker("parent", "3", layer: "0");
        var childMarker = Marker("child", "9", layer: "0");
        var otherLayer = Marker("other", "9", layer: "1");
        var categories = new List<MapCategory>
        {
            new() { Id = "3" },
            new() { Id = "9", ParentId = "3" }
        };

        var request = Request(parentMarker, childMarker, otherLayer) with
        {
            Categories = categories,
            SelectedCategoryId = "3",
            SelectedLayerId = "0"
        };

        var markers = MarkerScope.Visible(request);

        Assert.Equal(new[] { parentMarker, childMarker }, markers);
    }

    [Fact]
    public void RouteTargets_ExcludesCompletedTeleportAndAnchorOnly()
    {
        var near = Marker("near", "9", layer: "0", x: 15, y: 10);
        var far = Marker("far", "9", layer: "0", x: 40, y: 10);
        var completed = Marker("completed", "9", layer: "0", x: 12, y: 10);
        var teleport = Marker("teleport", "2", layer: "0", x: 11, y: 10);
        teleport.IsTeleport = true;

        var request = Request(near, far, completed, teleport) with
        {
            VisibleCategoryIds = new HashSet<string> { "2", "9" },
            CompletionOf = marker => marker.Id == "completed"
                ? new MarkerCompletion(true, true)
                : new MarkerCompletion(true, false)
        };

        var targets = MarkerScope.RouteTargets(request);

        Assert.Equal(new[] { near, far }, targets);
    }

    [Fact]
    public void CompletionCandidates_IgnoreHideCompletedAndUnknownState()
    {
        var completed = Marker("completed", "9", layer: "0");
        var unknown = Marker("unknown", "9", layer: "0");
        var request = Request(completed, unknown) with
        {
            VisibleCategoryIds = new HashSet<string> { "9" },
            HideCompleted = true,
            ShowUnknownCompletion = false,
            CompletionOf = marker => marker.Id == "completed"
                ? new MarkerCompletion(true, true)
                : default
        };

        var candidates = MarkerScope.CompletionCandidates(request);

        Assert.Equal(new[] { completed, unknown }, candidates);
    }

    private static MarkerScopeRequest Request(params MapMarker[] markers)
    {
        return new MarkerScopeRequest
        {
            Markers = markers,
            HideCompleted = false,
            ShowUnknownCompletion = true
        };
    }

    private static MapMarker Marker(string id, string category, string layer, double x = 0, double y = 0)
    {
        return new MapMarker
        {
            Id = id,
            CategoryId = category,
            CategoryIds = new List<string> { category },
            LayerId = layer,
            RegionId = "map-subtype-1",
            MapSubType = "1",
            Title = id,
            MapPosition = new MapPoint(x, y),
            WorldPosition = new MapPoint(x, y)
        };
    }
}
