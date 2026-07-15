using System;
using System.Collections.Generic;

namespace FarmTogether2.AutoSellMod
{
    internal readonly struct ExclusionMigrationDecision
    {
        internal ExclusionMigrationDecision(
            string excludedResources,
            int migrationVersion,
            bool excludedResourcesChanged)
        {
            ExcludedResources = excludedResources;
            MigrationVersion = migrationVersion;
            ExcludedResourcesChanged = excludedResourcesChanged;
        }

        internal string ExcludedResources { get; }
        internal int MigrationVersion { get; }
        internal bool ExcludedResourcesChanged { get; }
    }

    internal static class AutoSellPolicy
    {
        internal const int LegacyExclusionMigrationVersion = 1;
        internal const int CurrentMigrationVersion = 1;
        internal const uint MaxNativeInteractionCount = (uint)short.MaxValue;

        private static readonly char[] ExclusionSeparators =
            { ',', ';', ' ', '\t', '\r', '\n' };

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

        internal static bool ShouldMigrateLegacyExclusions(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in raw.Split(
                ExclusionSeparators,
                StringSplitOptions.RemoveEmptyEntries))
            {
                values.Add(token.Trim());
            }

            return values.Count == 3
                && values.Contains("Event")
                && values.Contains("EventB")
                && values.Contains("GoldNugget");
        }

        internal static ExclusionMigrationDecision DecideExclusionMigration(
            string? raw,
            int migrationVersion)
        {
            return DecideExclusionMigration(
                raw,
                migrationVersion,
                CurrentMigrationVersion);
        }

        internal static ExclusionMigrationDecision DecideExclusionMigration(
            string? raw,
            int migrationVersion,
            int currentMigrationVersion)
        {
            string excludedResources = raw ?? "";
            bool migrateLegacyDefault =
                migrationVersion < LegacyExclusionMigrationVersion
                && ShouldMigrateLegacyExclusions(excludedResources);
            int nextMigrationVersion = Math.Max(
                migrationVersion,
                currentMigrationVersion);
            return new ExclusionMigrationDecision(
                migrateLegacyDefault ? "GoldNugget" : excludedResources,
                nextMigrationVersion,
                migrateLegacyDefault);
        }

        internal static uint CalculateInteractionCount(
            long currentAmount,
            long maxValue,
            double triggerRatio,
            long amountPerInteraction,
            uint remainingUses,
            bool sellOneWhenFull)
        {
            if (currentAmount <= 0
                || maxValue <= 0
                || amountPerInteraction <= 0
                || remainingUses == 0
                || double.IsNaN(triggerRatio)
                || double.IsInfinity(triggerRatio)
                || triggerRatio <= 0.0
                || triggerRatio >= 1.0)
            {
                return 0;
            }

            if (currentAmount / (double)maxValue < triggerRatio)
                return 0;

            long targetAmount = (long)Math.Floor(maxValue * triggerRatio);
            long excessAmount = currentAmount - targetAmount;
            long possibleInteractions = excessAmount / amountPerInteraction;

            if (possibleInteractions == 0
                && sellOneWhenFull
                && currentAmount >= maxValue
                && currentAmount >= amountPerInteraction)
                possibleInteractions = 1;

            if (possibleInteractions <= 0)
                return 0;

            uint count = possibleInteractions >= MaxNativeInteractionCount
                ? MaxNativeInteractionCount
                : (uint)possibleInteractions;
            return count > remainingUses ? remainingUses : count;
        }

        internal static uint LimitInteractionCountForExecution(
            uint interactionCount,
            bool limitToSingleInteraction)
        {
            uint transportSafeCount = interactionCount > MaxNativeInteractionCount
                ? MaxNativeInteractionCount
                : interactionCount;
            return limitToSingleInteraction && transportSafeCount > 0
                ? 1u
                : transportSafeCount;
        }
    }
}
