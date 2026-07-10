using System;
using System.Collections.Generic;

namespace FarmTogether2.AutoSellMod
{
    internal readonly struct AutoSellOffer<TCandidate>
    {
        internal AutoSellOffer(TCandidate candidate, int currencyPriority)
        {
            Candidate = candidate;
            CurrencyPriority = currencyPriority;
        }

        internal TCandidate Candidate { get; }
        internal int CurrencyPriority { get; }
    }

    internal sealed class AutoSellDispatcher<TCandidate>
    {
        private sealed class QueuedCandidate
        {
            internal QueuedCandidate(
                TCandidate candidate,
                int currencyPriority,
                int discoveryOrder)
            {
                Candidate = candidate;
                CurrencyPriority = currencyPriority;
                DiscoveryOrder = discoveryOrder;
            }

            internal TCandidate Candidate { get; }
            internal int CurrencyPriority { get; }
            internal int DiscoveryOrder { get; }
        }

        private readonly List<QueuedCandidate> _candidates = new List<QueuedCandidate>();
        private int _nextDiscoveryOrder;

        internal void Clear()
        {
            _candidates.Clear();
            _nextDiscoveryOrder = 0;
        }

        internal void CollectOffers(
            int offerCount,
            Func<int, AutoSellOffer<TCandidate>?> collectOffer,
            Action<Exception> onCollectionFailure)
        {
            for (int offerIndex = 0; offerIndex < offerCount; offerIndex++)
            {
                try
                {
                    AutoSellOffer<TCandidate>? offer = collectOffer(offerIndex);
                    if (!offer.HasValue)
                        continue;

                    AutoSellOffer<TCandidate> value = offer.Value;
                    _candidates.Add(new QueuedCandidate(
                        value.Candidate,
                        value.CurrencyPriority,
                        _nextDiscoveryOrder++));
                }
                catch (Exception exception)
                {
                    onCollectionFailure(exception);
                }
            }
        }

        internal void ExecuteCandidates(
            Action<TCandidate> executeCandidate,
            Action<Exception> onExecutionFailure)
        {
            _candidates.Sort(CompareCandidates);

            for (int candidateIndex = 0; candidateIndex < _candidates.Count; candidateIndex++)
            {
                try
                {
                    executeCandidate(_candidates[candidateIndex].Candidate);
                }
                catch (Exception exception)
                {
                    onExecutionFailure(exception);
                }
            }
        }

        private static int CompareCandidates(QueuedCandidate left, QueuedCandidate right)
        {
            return AutoSellPolicy.CompareOffers(
                left.CurrencyPriority,
                left.DiscoveryOrder,
                right.CurrencyPriority,
                right.DiscoveryOrder);
        }
    }
}
