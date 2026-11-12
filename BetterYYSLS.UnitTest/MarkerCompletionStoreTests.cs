using BetterYYSLS.Core.Map;
using Xunit;

namespace BetterYYSLS.UnitTest;

public sealed class MarkerCompletionStoreTests
{
    [Fact]
    public void Get_ReturnsUnknownWhenRoleHasNoEntry()
    {
        var store = new MarkerCompletionStore();

        var completion = store.Get("role-a", "marker-1");

        Assert.False(completion.IsKnown);
        Assert.False(completion.IsCompleted);
    }

    [Fact]
    public void CompletionIsRoleScoped()
    {
        var store = new MarkerCompletionStore();
        store.Set("role-a", "marker-1", new MarkerCompletion(true, true));
        store.Set("role-b", "marker-1", new MarkerCompletion(true, false));

        Assert.True(store.Get("role-a", "marker-1").IsCompleted);
        Assert.False(store.Get("role-b", "marker-1").IsCompleted);
        Assert.False(store.Get("role-c", "marker-1").IsKnown);
    }

    [Fact]
    public void Clear_RemovesOnlyRequestedRole()
    {
        var store = new MarkerCompletionStore();
        store.Set("role-a", "marker-1", new MarkerCompletion(true, true));
        store.Set("role-b", "marker-1", new MarkerCompletion(true, true));

        store.Clear("role-a");

        Assert.False(store.Get("role-a", "marker-1").IsKnown);
        Assert.True(store.Get("role-b", "marker-1").IsCompleted);
    }
}
