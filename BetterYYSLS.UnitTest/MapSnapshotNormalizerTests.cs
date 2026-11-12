using BetterYYSLS.Core.Map;
using Xunit;

namespace BetterYYSLS.UnitTest;

public sealed class MapSnapshotNormalizerTests
{
    [Fact]
    public void Normalize_DeduplicatesMarkerIdentity()
    {
        var snapshot = new MapSnapshot
        {
            Markers =
            [
                new MapMarker { Id = "a", Title = "first" },
                new MapMarker { Id = "a", Title = "second" },
                new MapMarker { Id = "", Title = "invalid" }
            ]
        };

        var normalized = MapSnapshotNormalizer.Normalize(snapshot);

        Assert.Single(normalized.Markers);
        Assert.Equal("first", normalized.Markers[0].Title);
    }
}
