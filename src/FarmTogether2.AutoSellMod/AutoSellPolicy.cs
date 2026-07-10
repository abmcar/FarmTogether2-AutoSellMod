namespace FarmTogether2.AutoSellMod
{
    internal static class AutoSellPolicy
    {
        internal static int GetCurrencyPriority(long coins, long bills, long medals)
        {
            if (medals > 0)
                return 3;
            if (bills > 0)
                return 2;
            if (coins > 0)
                return 1;
            return 0;
        }

        internal static int CompareOffers(
            int leftPriority,
            int leftOrder,
            int rightPriority,
            int rightOrder)
        {
            int priorityComparison = rightPriority.CompareTo(leftPriority);
            return priorityComparison != 0
                ? priorityComparison
                : leftOrder.CompareTo(rightOrder);
        }
    }
}
