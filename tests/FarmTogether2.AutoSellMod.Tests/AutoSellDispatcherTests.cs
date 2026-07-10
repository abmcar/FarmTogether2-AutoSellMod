using System;
using System.Collections.Generic;
using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellDispatcherTests
{
    [Fact]
    public void CollectionContinuesAfterOneOfferInTheSameShopThrows()
    {
        var dispatcher = new AutoSellDispatcher<TestCandidate>();
        var collectionFailures = new List<Exception>();
        var executionFailures = new List<Exception>();
        var executed = new List<string>();

        dispatcher.CollectOffers(
            offerCount: 3,
            collectOffer: index =>
            {
                if (index == 0)
                    throw new InvalidOperationException("broken first offer");

                return new AutoSellOffer<TestCandidate>(
                    new TestCandidate($"offer-{index}", AmountPerInteraction: 10),
                    currencyPriority: 1);
            },
            onCollectionFailure: collectionFailures.Add);

        dispatcher.ExecuteCandidates(
            candidate => executed.Add(candidate.Id),
            executionFailures.Add);

        Assert.Equal(new[] { "offer-1", "offer-2" }, executed);
        Assert.Single(collectionFailures);
        Assert.Empty(executionFailures);
    }

    [Fact]
    public void ExecutionSortsMedalsBeforeBillsBeforeCoins()
    {
        var dispatcher = new AutoSellDispatcher<TestCandidate>();
        var offers = new[]
        {
            new AutoSellOffer<TestCandidate>(
                new TestCandidate("coins", 10),
                AutoSellPolicy.GetCurrencyPriority(coins: 1, bills: 0, medals: 0)),
            new AutoSellOffer<TestCandidate>(
                new TestCandidate("medals", 10),
                AutoSellPolicy.GetCurrencyPriority(coins: 0, bills: 0, medals: 1)),
            new AutoSellOffer<TestCandidate>(
                new TestCandidate("bills", 10),
                AutoSellPolicy.GetCurrencyPriority(coins: 0, bills: 1, medals: 0)),
        };
        var executed = new List<string>();
        var failures = new List<Exception>();

        dispatcher.CollectOffers(
            offers.Length,
            index => offers[index],
            failures.Add);
        dispatcher.ExecuteCandidates(
            candidate => executed.Add(candidate.Id),
            failures.Add);

        Assert.Equal(new[] { "medals", "bills", "coins" }, executed);
        Assert.Empty(failures);
    }

    [Fact]
    public void ExecutionContinuesAfterOneCandidateThrows()
    {
        var dispatcher = new AutoSellDispatcher<TestCandidate>();
        var attempted = new List<string>();
        var executionFailures = new List<Exception>();

        dispatcher.CollectOffers(
            offerCount: 2,
            index => new AutoSellOffer<TestCandidate>(
                new TestCandidate($"candidate-{index}", 10),
                currencyPriority: 1),
            _ => throw new InvalidOperationException("collection should not fail"));

        dispatcher.ExecuteCandidates(
            candidate =>
            {
                attempted.Add(candidate.Id);
                if (candidate.Id == "candidate-0")
                    throw new InvalidOperationException("broken first candidate");
            },
            executionFailures.Add);

        Assert.Equal(new[] { "candidate-0", "candidate-1" }, attempted);
        Assert.Single(executionFailures);
    }

    [Fact]
    public void ExecutionCallbacksReadMutatedInventoryAndQuotaWithoutCrossingTarget()
    {
        var dispatcher = new AutoSellDispatcher<TestCandidate>();
        var offers = new[]
        {
            new AutoSellOffer<TestCandidate>(new TestCandidate("coins", 10), currencyPriority: 1),
            new AutoSellOffer<TestCandidate>(new TestCandidate("bills", 10), currencyPriority: 2),
            new AutoSellOffer<TestCandidate>(new TestCandidate("medals", 150), currencyPriority: 3),
        };
        long inventory = 1000;
        var remainingUses = new Dictionary<string, uint>
        {
            ["coins"] = 100,
            ["bills"] = 5,
            ["medals"] = 1,
        };
        var observations = new List<(string Id, long Inventory, uint RemainingUses)>();
        var failures = new List<Exception>();

        dispatcher.CollectOffers(offers.Length, index => offers[index], failures.Add);
        dispatcher.ExecuteCandidates(
            candidate =>
            {
                long currentInventory = inventory;
                uint currentRemainingUses = remainingUses[candidate.Id];
                observations.Add((candidate.Id, currentInventory, currentRemainingUses));

                uint interactionCount = AutoSellPolicy.CalculateInteractionCount(
                    currentInventory,
                    maxValue: 1000,
                    triggerRatio: 0.80,
                    candidate.AmountPerInteraction,
                    currentRemainingUses,
                    sellOneWhenFull: false);
                inventory -= candidate.AmountPerInteraction * interactionCount;
                remainingUses[candidate.Id] -= interactionCount;

                if (candidate.Id == "medals")
                    remainingUses["bills"] = 0;
            },
            failures.Add);

        Assert.Equal(
            new[]
            {
                (Id: "medals", Inventory: 1000L, RemainingUses: 1u),
                (Id: "bills", Inventory: 850L, RemainingUses: 0u),
                (Id: "coins", Inventory: 850L, RemainingUses: 100u),
            },
            observations);
        Assert.Equal(800, inventory);
        Assert.Empty(failures);
    }

    private readonly record struct TestCandidate(string Id, long AmountPerInteraction);
}
