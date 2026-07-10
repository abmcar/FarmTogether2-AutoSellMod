using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellPolicyTests
{
    [Theory]
    [InlineData(0, 0, 1, 3)]
    [InlineData(0, 1, 0, 2)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(50, 25, 2, 3)]
    public void CurrencyPriorityUsesHighestRewardCurrency(
        long coins,
        long bills,
        long medals,
        int expected)
    {
        Assert.Equal(expected, AutoSellPolicy.GetCurrencyPriority(coins, bills, medals));
    }

    [Fact]
    public void OfferComparisonSortsByPriorityThenDiscoveryOrder()
    {
        Assert.True(AutoSellPolicy.CompareOffers(3, 9, 2, 0) < 0);
        Assert.True(AutoSellPolicy.CompareOffers(2, 0, 3, 9) > 0);
        Assert.True(AutoSellPolicy.CompareOffers(2, 4, 2, 7) < 0);
        Assert.True(AutoSellPolicy.CompareOffers(2, 7, 2, 4) > 0);
        Assert.Equal(0, AutoSellPolicy.CompareOffers(1, 5, 1, 5));
    }

    [Theory]
    [InlineData("Event,EventB,GoldNugget")]
    [InlineData("goldnugget; eventb EVENT")]
    [InlineData(" Event\r\nGoldNugget\tEventB ")]
    public void LegacyDefaultExclusionsAreRecognized(string raw)
    {
        Assert.True(AutoSellPolicy.ShouldMigrateLegacyExclusions(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GoldNugget")]
    [InlineData("Event,EventB,GoldNugget,Spice")]
    [InlineData("Event,GoldNugget")]
    public void CustomizedExclusionsAreNotMigrated(string? raw)
    {
        Assert.False(AutoSellPolicy.ShouldMigrateLegacyExclusions(raw));
    }

    [Fact]
    public void FirstLoadMigratesLegacyDefaultAndMarksMigrationComplete()
    {
        ExclusionMigrationDecision decision =
            AutoSellPolicy.DecideExclusionMigration("Event,EventB,GoldNugget", migrationVersion: 0);

        Assert.Equal("GoldNugget", decision.ExcludedResources);
        Assert.Equal(AutoSellPolicy.CurrentMigrationVersion, decision.MigrationVersion);
        Assert.True(decision.ExcludedResourcesChanged);
    }

    [Theory]
    [InlineData("GoldNugget")]
    [InlineData("GoldNugget,Spice")]
    public void FirstLoadPreservesNonLegacyExclusionsAndMarksMigrationComplete(string raw)
    {
        ExclusionMigrationDecision decision =
            AutoSellPolicy.DecideExclusionMigration(raw, migrationVersion: 0);

        Assert.Equal(raw, decision.ExcludedResources);
        Assert.Equal(AutoSellPolicy.CurrentMigrationVersion, decision.MigrationVersion);
        Assert.False(decision.ExcludedResourcesChanged);
    }

    [Fact]
    public void CompletedMigrationPreservesReintroducedEventExclusionsOnSecondLoad()
    {
        ExclusionMigrationDecision firstLoad =
            AutoSellPolicy.DecideExclusionMigration("Event,EventB,GoldNugget", migrationVersion: 0);
        const string userExclusions = "Event,EventB,GoldNugget";

        ExclusionMigrationDecision secondLoad =
            AutoSellPolicy.DecideExclusionMigration(userExclusions, firstLoad.MigrationVersion);

        Assert.Equal(userExclusions, secondLoad.ExcludedResources);
        Assert.Equal(AutoSellPolicy.CurrentMigrationVersion, secondLoad.MigrationVersion);
        Assert.False(secondLoad.ExcludedResourcesChanged);
    }

    [Fact]
    public void InteractionCountSellsOnlyExcessAndRespectsRemainingUses()
    {
        Assert.Equal(
            2u,
            AutoSellPolicy.CalculateInteractionCount(850, 1000, 0.80, 20, 2, false));
    }

    [Fact]
    public void FullStorageCanForceOneTrade()
    {
        Assert.Equal(
            1u,
            AutoSellPolicy.CalculateInteractionCount(100, 100, 0.95, 10, 5, true));
        Assert.Equal(
            0u,
            AutoSellPolicy.CalculateInteractionCount(100, 100, 0.95, 10, 5, false));
    }

    [Fact]
    public void PreferredOfferCapacityFallsBackWithoutCrossingTarget()
    {
        var offers = new[]
        {
            new Offer(Priority: 1, Order: 0, AmountPerInteraction: 10, RemainingUses: 100),
            new Offer(Priority: 3, Order: 1, AmountPerInteraction: 150, RemainingUses: 1),
            new Offer(Priority: 2, Order: 2, AmountPerInteraction: 10, RemainingUses: 100),
        };

        System.Array.Sort(
            offers,
            (left, right) => AutoSellPolicy.CompareOffers(
                left.Priority,
                left.Order,
                right.Priority,
                right.Order));

        long currentAmount = 1000;
        var executed = new System.Collections.Generic.List<(int Order, uint Count)>();

        foreach (Offer offer in offers)
        {
            uint count = AutoSellPolicy.CalculateInteractionCount(
                currentAmount,
                maxValue: 1000,
                triggerRatio: 0.80,
                offer.AmountPerInteraction,
                offer.RemainingUses,
                sellOneWhenFull: false);

            if (count == 0)
                continue;

            executed.Add((offer.Order, count));
            currentAmount -= offer.AmountPerInteraction * count;
        }

        Assert.Equal(new[] { (Order: 1, Count: 1u), (Order: 2, Count: 5u) }, executed);
        Assert.Equal(800, currentAmount);
    }

    private readonly record struct Offer(
        int Priority,
        int Order,
        long AmountPerInteraction,
        uint RemainingUses);
}
