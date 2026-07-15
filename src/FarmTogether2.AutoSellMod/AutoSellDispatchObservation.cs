using System;
using System.Collections.Generic;

namespace FarmTogether2.AutoSellMod
{
    internal readonly struct AutoSellMoneySignature : IEquatable<AutoSellMoneySignature>
    {
        internal AutoSellMoneySignature(long coins, long bills, long medals)
        {
            Coins = coins;
            Bills = bills;
            Medals = medals;
        }

        internal long Coins { get; }
        internal long Bills { get; }
        internal long Medals { get; }

        internal void ValidateMultiplicationRange(uint interactionCount)
        {
            if (interactionCount == 0)
                throw new ArgumentOutOfRangeException(nameof(interactionCount));

            long multiplier = interactionCount;
            _ = checked(Coins * multiplier);
            _ = checked(Bills * multiplier);
            _ = checked(Medals * multiplier);
        }

        public bool Equals(AutoSellMoneySignature other)
        {
            return Coins == other.Coins
                && Bills == other.Bills
                && Medals == other.Medals;
        }

        public override bool Equals(object? obj)
        {
            return obj is AutoSellMoneySignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Coins, Bills, Medals);
        }
    }

    internal static class AutoSellNativeMoneyProjection
    {
        internal static AutoSellMoneySignature Project(
            uint interactionCount,
            Func<float, AutoSellMoneySignature> nativeMultiply)
        {
            if (interactionCount == 0)
                throw new ArgumentOutOfRangeException(nameof(interactionCount));
            if (nativeMultiply == null)
                throw new ArgumentNullException(nameof(nativeMultiply));

            return nativeMultiply((float)interactionCount);
        }
    }

    internal readonly struct AutoSellDispatchSignature<TKey> : IEquatable<AutoSellDispatchSignature<TKey>>
        where TKey : notnull
    {
        internal AutoSellDispatchSignature(
            long sessionGeneration,
            TKey resource,
            long resourceAmount,
            AutoSellMoneySignature money)
        {
            if (sessionGeneration <= 0)
                throw new ArgumentOutOfRangeException(nameof(sessionGeneration));
            if (resource is null)
                throw new ArgumentNullException(nameof(resource));
            if (resourceAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(resourceAmount));

            SessionGeneration = sessionGeneration;
            Resource = resource;
            ResourceAmount = resourceAmount;
            Money = money;
        }

        internal long SessionGeneration { get; }
        internal TKey Resource { get; }
        internal long ResourceAmount { get; }
        internal AutoSellMoneySignature Money { get; }

        public bool Equals(AutoSellDispatchSignature<TKey> other)
        {
            return SessionGeneration == other.SessionGeneration
                && EqualityComparer<TKey>.Default.Equals(Resource, other.Resource)
                && ResourceAmount == other.ResourceAmount
                && Money.Equals(other.Money);
        }

        public override bool Equals(object? obj)
        {
            return obj is AutoSellDispatchSignature<TKey> other && Equals(other);
        }

        public override int GetHashCode()
        {
            int resourceHashCode = Resource is null
                ? 0
                : EqualityComparer<TKey>.Default.GetHashCode(Resource);
            return HashCode.Combine(
                SessionGeneration,
                resourceHashCode,
                ResourceAmount,
                Money);
        }
    }

    internal sealed class AutoSellDispatchObservation<TKey>
        where TKey : notnull
    {
        private readonly object _sync = new object();
        private bool _hasActiveDispatch;
        private AutoSellDispatchSignature<TKey> _activeSignature;
        private bool _dispatchEntered;
        private int _dispatchThreadId;
        private bool _matchingCallbackObserved;

        internal bool HasActiveDispatch
        {
            get
            {
                lock (_sync)
                {
                    return _hasActiveDispatch;
                }
            }
        }

        internal bool TryBeginDispatch(AutoSellDispatchSignature<TKey> signature)
        {
            lock (_sync)
            {
                if (_hasActiveDispatch)
                    return false;

                _activeSignature = signature;
                _dispatchEntered = false;
                _dispatchThreadId = 0;
                _matchingCallbackObserved = false;
                _hasActiveDispatch = true;
                return true;
            }
        }

        internal bool TryObserveCallback(AutoSellDispatchSignature<TKey> signature)
        {
            lock (_sync)
            {
                if (!_hasActiveDispatch
                    || !_dispatchEntered
                    || _dispatchThreadId != Environment.CurrentManagedThreadId
                    || !_activeSignature.Equals(signature))
                    return false;

                _matchingCallbackObserved = true;
                return true;
            }
        }

        internal bool TryExecuteDispatch<TState>(
            AutoSellDispatchSignature<TKey> signature,
            Action<TState> dispatch,
            TState state,
            out bool matchingCallbackObserved)
        {
            if (dispatch == null)
                throw new ArgumentNullException(nameof(dispatch));

            int dispatchThreadId = Environment.CurrentManagedThreadId;
            lock (_sync)
            {
                if (!_hasActiveDispatch || !_activeSignature.Equals(signature))
                {
                    matchingCallbackObserved = false;
                    return false;
                }

                _dispatchEntered = true;
                _dispatchThreadId = dispatchThreadId;
            }

            try
            {
                dispatch(state);
            }
            catch
            {
                TryAbortDispatch(signature);
                throw;
            }

            lock (_sync)
            {
                if (!_hasActiveDispatch || !_activeSignature.Equals(signature))
                {
                    matchingCallbackObserved = false;
                    return false;
                }
                if (!_dispatchEntered || _dispatchThreadId != dispatchThreadId)
                {
                    matchingCallbackObserved = false;
                    return false;
                }

                matchingCallbackObserved = _matchingCallbackObserved;
                ClearWithoutLock();
                return true;
            }
        }

        internal bool TryAbortDispatch(AutoSellDispatchSignature<TKey> signature)
        {
            lock (_sync)
            {
                if (!_hasActiveDispatch || !_activeSignature.Equals(signature))
                    return false;

                ClearWithoutLock();
                return true;
            }
        }

        internal void Clear()
        {
            lock (_sync)
            {
                ClearWithoutLock();
            }
        }

        private void ClearWithoutLock()
        {
            _hasActiveDispatch = false;
            _activeSignature = default;
            _dispatchEntered = false;
            _dispatchThreadId = 0;
            _matchingCallbackObserved = false;
        }
    }
}
