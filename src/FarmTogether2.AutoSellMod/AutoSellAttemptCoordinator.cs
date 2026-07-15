using System;

namespace FarmTogether2.AutoSellMod
{
    internal enum AutoSellAttemptOutcome
    {
        NoAttempt,
        BlockedByPendingRequest,
        BlockedByActiveDispatch,
        Dispatched,
    }

    internal readonly struct PreparedAutoSellAttempt<TPayload>
    {
        internal PreparedAutoSellAttempt(
            long expectedResourceAmount,
            AutoSellMoneySignature expectedMoney,
            TPayload payload)
        {
            if (expectedResourceAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedResourceAmount));

            ExpectedResourceAmount = expectedResourceAmount;
            ExpectedMoney = expectedMoney;
            Payload = payload;
        }

        internal long ExpectedResourceAmount { get; }
        internal AutoSellMoneySignature ExpectedMoney { get; }
        internal TPayload Payload { get; }
    }

    internal sealed class AutoSellAttemptCoordinator<TKey, TPayload>
        where TKey : notnull
    {
        private readonly AutoSellPendingTracker<TKey, TPayload> _pendingRequests;
        private readonly AutoSellDispatchObservation<TKey> _dispatchObservation;

        internal AutoSellAttemptCoordinator(
            AutoSellPendingTracker<TKey, TPayload> pendingRequests)
        {
            _pendingRequests = pendingRequests
                ?? throw new ArgumentNullException(nameof(pendingRequests));
            _dispatchObservation = new AutoSellDispatchObservation<TKey>();
        }

        internal AutoSellAttemptOutcome TryPrepareAndDispatch(
            long sessionGeneration,
            TKey resource,
            double now,
            double confirmationTimeoutSeconds,
            Func<PreparedAutoSellAttempt<TPayload>?> prepare,
            Action<PreparedAutoSellAttempt<TPayload>> dispatch,
            out PreparedAutoSellAttempt<TPayload> prepared,
            out PendingSale<TKey, TPayload>? confirmed)
        {
            if (sessionGeneration <= 0)
                throw new ArgumentOutOfRangeException(nameof(sessionGeneration));
            if (prepare == null)
                throw new ArgumentNullException(nameof(prepare));
            if (dispatch == null)
                throw new ArgumentNullException(nameof(dispatch));
            if (!IsFinite(now))
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!IsFinite(confirmationTimeoutSeconds) || confirmationTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(confirmationTimeoutSeconds));

            confirmed = null;

            if (_dispatchObservation.HasActiveDispatch)
            {
                prepared = default;
                return AutoSellAttemptOutcome.BlockedByActiveDispatch;
            }

            if (_pendingRequests.IsPending(resource))
            {
                prepared = default;
                return AutoSellAttemptOutcome.BlockedByPendingRequest;
            }

            PreparedAutoSellAttempt<TPayload>? proposal = prepare();
            if (!proposal.HasValue)
            {
                prepared = default;
                return AutoSellAttemptOutcome.NoAttempt;
            }

            prepared = proposal.Value;
            var signature = new AutoSellDispatchSignature<TKey>(
                sessionGeneration,
                resource,
                prepared.ExpectedResourceAmount,
                prepared.ExpectedMoney);
            if (!_dispatchObservation.TryBeginDispatch(signature))
            {
                prepared = default;
                return AutoSellAttemptOutcome.BlockedByActiveDispatch;
            }

            bool pendingStarted;
            try
            {
                pendingStarted = _pendingRequests.TryBegin(
                    signature,
                    now,
                    confirmationTimeoutSeconds,
                    prepared.Payload);
            }
            catch
            {
                _dispatchObservation.TryAbortDispatch(signature);
                throw;
            }

            if (!pendingStarted)
            {
                _dispatchObservation.TryAbortDispatch(signature);
                prepared = default;
                return AutoSellAttemptOutcome.BlockedByPendingRequest;
            }

            try
            {
                if (!_dispatchObservation.TryExecuteDispatch(
                        signature,
                        dispatch,
                        prepared,
                        out bool matchingCallbackObserved))
                {
                    throw new InvalidOperationException(
                        "AutoSell dispatch observation was reset before dispatch completed.");
                }

                if (matchingCallbackObserved)
                {
                    if (!_pendingRequests.TryCommitObservedConfirmation(
                            signature,
                            out PendingSale<TKey, TPayload> committed))
                    {
                        throw new InvalidOperationException(
                            "AutoSell observed a matching callback but could not commit its pending request.");
                    }

                    confirmed = committed;
                }
            }
            catch
            {
                // A native exception does not prove that the request had no side effects.
                // Discard any callback observation but deliberately retain pending state.
                _dispatchObservation.TryAbortDispatch(signature);
                throw;
            }

            return AutoSellAttemptOutcome.Dispatched;
        }

        internal bool TryObserveCallback(AutoSellDispatchSignature<TKey> signature)
        {
            return _dispatchObservation.TryObserveCallback(signature);
        }

        internal void Clear()
        {
            _dispatchObservation.Clear();
            _pendingRequests.Clear();
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
