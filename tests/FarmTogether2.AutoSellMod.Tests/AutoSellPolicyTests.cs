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
}
