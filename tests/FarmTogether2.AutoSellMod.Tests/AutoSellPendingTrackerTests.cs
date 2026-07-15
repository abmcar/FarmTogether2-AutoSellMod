using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellPendingTrackerTests
{
    [Fact]
    public void SameResourceIsBlockedUntilTheExactObservedSignatureIsCommitted()
    {
        var tracker = new AutoSellPendingTracker<string, string>();
        AutoSellDispatchSignature<string> first = Signature(7, "Apple", 20, coins: 40);

        Assert.True(tracker.TryBegin(first, 0, 10, "first"));
        Assert.False(tracker.TryBegin(first, 1, 10, "duplicate"));
        Assert.True(tracker.TryBegin(Signature(7, "Milk", 10, bills: 2), 1, 10, "other-resource"));

        Assert.False(tracker.TryCommitObservedConfirmation(
            Signature(7, "Apple", 10, coins: 40),
            out _));
        Assert.False(tracker.TryCommitObservedConfirmation(
            Signature(7, "Apple", 20, coins: 41),
            out _));
        Assert.False(tracker.TryCommitObservedConfirmation(
            Signature(8, "Apple", 20, coins: 40),
            out _));
        Assert.True(tracker.IsPending("Apple"));

        Assert.True(tracker.TryCommitObservedConfirmation(
            first,
            out PendingSale<string, string> confirmed));
        Assert.Equal("first", confirmed.Payload);
        Assert.Equal(7, confirmed.SessionGeneration);
        Assert.Equal(new AutoSellMoneySignature(40, 0, 0), confirmed.ExpectedMoney);
        Assert.False(tracker.IsPending("Apple"));
        Assert.True(tracker.TryBegin(Signature(7, "Apple", 20, coins: 40), 2, 10, "next"));
    }

    [Fact]
    public void TimeoutMakesRequestUncertainAndKeepsItPending()
    {
        var tracker = new AutoSellPendingTracker<string, string>();
        Assert.True(tracker.TryBegin(Signature(7, "Apple", 20, coins: 40), 0, 10, "first"));

        Assert.Empty(tracker.CollectNewTimeouts(9.999));
        Assert.False(tracker.IsUncertain("Apple"));
        Assert.Single(tracker.CollectNewTimeouts(10));
        Assert.True(tracker.IsUncertain("Apple"));
        Assert.Empty(tracker.CollectNewTimeouts(1000));
        Assert.False(tracker.TryBegin(
            Signature(7, "Apple", 20, coins: 40),
            1000,
            10,
            "retry-is-forbidden"));
        Assert.True(tracker.IsPending("Apple"));
    }

    [Fact]
    public void LifecycleResetClearsAnUncertainRequest()
    {
        var tracker = new AutoSellPendingTracker<string, string>();
        Assert.True(tracker.TryBegin(Signature(7, "Apple", 20, coins: 40), 5, 10, "first"));

        Assert.Single(tracker.CollectNewTimeouts(15));
        Assert.True(tracker.IsUncertain("Apple"));
        tracker.Clear();
        Assert.False(tracker.IsPending("Apple"));
        Assert.True(tracker.TryBegin(Signature(8, "Apple", 10, coins: 20), 35, 10, "new-session"));
    }

    [Fact]
    public void DifferentResourcesRemainIndependentAfterTimeout()
    {
        var tracker = new AutoSellPendingTracker<string, string>();
        Assert.True(tracker.TryBegin(Signature(7, "Apple", 20, coins: 40), 0, 10, "first"));
        Assert.True(tracker.TryBegin(Signature(7, "Milk", 10, bills: 2), 1, 10, "other"));

        Assert.Single(tracker.CollectNewTimeouts(10));
        Assert.True(tracker.IsUncertain("Apple"));
        Assert.False(tracker.IsUncertain("Milk"));
        Assert.True(tracker.IsPending("Apple"));
        Assert.True(tracker.IsPending("Milk"));
    }

    private static AutoSellDispatchSignature<string> Signature(
        long generation,
        string resource,
        long amount,
        long coins = 0,
        long bills = 0,
        long medals = 0)
    {
        return new AutoSellDispatchSignature<string>(
            generation,
            resource,
            amount,
            new AutoSellMoneySignature(coins, bills, medals));
    }
}
