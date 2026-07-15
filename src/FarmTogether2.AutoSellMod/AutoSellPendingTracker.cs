using System;
using System.Collections.Generic;

namespace FarmTogether2.AutoSellMod
{
    internal readonly struct PendingSale<TKey, TPayload>
        where TKey : notnull
    {
        internal PendingSale(
            AutoSellDispatchSignature<TKey> signature,
            double submittedAt,
            double timeoutAt,
            TPayload payload)
        {
            Signature = signature;
            SubmittedAt = submittedAt;
            TimeoutAt = timeoutAt;
            Payload = payload;
        }

        internal AutoSellDispatchSignature<TKey> Signature { get; }
        internal long SessionGeneration => Signature.SessionGeneration;
        internal TKey Resource => Signature.Resource;
        internal long ExpectedResourceAmount => Signature.ResourceAmount;
        internal AutoSellMoneySignature ExpectedMoney => Signature.Money;
        internal double SubmittedAt { get; }
        internal double TimeoutAt { get; }
        internal TPayload Payload { get; }
    }

    internal sealed class AutoSellPendingTracker<TKey, TPayload>
        where TKey : notnull
    {
        private sealed class Entry
        {
            internal Entry(PendingSale<TKey, TPayload> sale)
            {
                Sale = sale;
            }

            internal PendingSale<TKey, TPayload> Sale { get; }
            internal bool TimeoutReported { get; set; }
        }

        private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>();

        internal int Count => _entries.Count;

        internal bool IsPending(TKey resource)
        {
            return _entries.ContainsKey(resource);
        }

        internal bool IsUncertain(TKey resource)
        {
            return _entries.TryGetValue(resource, out Entry? entry)
                && entry.TimeoutReported;
        }

        internal bool TryBegin(
            AutoSellDispatchSignature<TKey> signature,
            double now,
            double confirmationTimeoutSeconds,
            TPayload payload)
        {
            if (!IsFinite(now))
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!IsFinite(confirmationTimeoutSeconds) || confirmationTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(confirmationTimeoutSeconds));

            if (_entries.ContainsKey(signature.Resource))
                return false;

            double timeoutAt = now + confirmationTimeoutSeconds;
            if (!IsFinite(timeoutAt))
                throw new ArgumentOutOfRangeException(nameof(now));

            var pending = new PendingSale<TKey, TPayload>(
                signature,
                now,
                timeoutAt,
                payload);
            _entries.Add(signature.Resource, new Entry(pending));
            return true;
        }

        internal bool TryCommitObservedConfirmation(
            AutoSellDispatchSignature<TKey> signature,
            out PendingSale<TKey, TPayload> confirmed)
        {
            if (_entries.TryGetValue(signature.Resource, out Entry? entry)
                && signature.Equals(entry.Sale.Signature))
            {
                _entries.Remove(signature.Resource);
                confirmed = entry.Sale;
                return true;
            }

            confirmed = default;
            return false;
        }

        internal IReadOnlyList<PendingSale<TKey, TPayload>> CollectNewTimeouts(double now)
        {
            if (!IsFinite(now))
                throw new ArgumentOutOfRangeException(nameof(now));

            var timedOut = new List<PendingSale<TKey, TPayload>>();

            foreach (Entry entry in _entries.Values)
            {
                if (entry.TimeoutReported || now < entry.Sale.TimeoutAt)
                    continue;

                entry.TimeoutReported = true;
                timedOut.Add(entry.Sale);
            }

            return timedOut;
        }

        internal void Clear()
        {
            _entries.Clear();
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
