using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellAttemptCoordinatorTests
{
    [Fact]
    public void InvalidFirstCandidateDoesNotClaimResourceBeforeSecondCandidateIsPrepared()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        var dispatched = new List<string>();

        AutoSellAttemptOutcome first = coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => null,
            dispatch: _ => throw new InvalidOperationException("invalid candidates are not dispatched"),
            out _,
            out _);
        AutoSellAttemptOutcome second = coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => Attempt(20, "second", coins: 40),
            dispatch: attempt =>
            {
                Assert.True(pending.IsPending("Apple"));
                dispatched.Add(attempt.Payload);
            },
            out PreparedAutoSellAttempt<string> prepared,
            out PendingSale<string, string>? confirmed);

        Assert.Equal(AutoSellAttemptOutcome.NoAttempt, first);
        Assert.Equal(AutoSellAttemptOutcome.Dispatched, second);
        Assert.Equal(20, prepared.ExpectedResourceAmount);
        Assert.Equal(new[] { "second" }, dispatched);
        Assert.Null(confirmed);
        Assert.True(pending.IsPending("Apple"));
    }

    [Fact]
    public void PreparationExceptionDoesNotArmPendingState()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);

        Assert.Throws<InvalidOperationException>(() => coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => throw new InvalidOperationException("validation failed"),
            dispatch: _ => throw new InvalidOperationException("invalid attempts are not dispatched"),
            out _,
            out _));

        Assert.False(pending.IsPending("Apple"));
    }

    [Fact]
    public void PendingValidationExceptionDoesNotLeaveAnActiveDispatchWindow()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);

        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: double.MaxValue,
            confirmationTimeoutSeconds: double.MaxValue,
            prepare: () => Attempt(20, "overflow", coins: 40),
            dispatch: _ => { },
            out _,
            out _));

        Assert.Equal(
            AutoSellAttemptOutcome.Dispatched,
            coordinator.TryPrepareAndDispatch(
                7,
                "Milk",
                now: 0,
                confirmationTimeoutSeconds: 15,
                prepare: () => Attempt(10, "valid", bills: 2),
                dispatch: _ => { },
                out _,
                out _));
    }

    [Fact]
    public void ArmedRequestBlocksEveryLaterCandidateWithoutPreparingIt()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        Assert.Equal(
            AutoSellAttemptOutcome.Dispatched,
            coordinator.TryPrepareAndDispatch(
                7,
                "Apple",
                now: 0,
                confirmationTimeoutSeconds: 15,
                prepare: () => Attempt(20, "first", coins: 40),
                dispatch: _ => { },
                out _,
                out _));

        int prepareCalls = 0;
        AutoSellAttemptOutcome blocked = coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 1,
            confirmationTimeoutSeconds: 15,
            prepare: () =>
            {
                prepareCalls++;
                return Attempt(20, "duplicate", coins: 40);
            },
            dispatch: _ => throw new InvalidOperationException("blocked requests are not dispatched"),
            out _,
            out _);

        Assert.Equal(AutoSellAttemptOutcome.BlockedByPendingRequest, blocked);
        Assert.Equal(0, prepareCalls);
    }

    [Fact]
    public void TimedOutRequestRemainsBlockedAcrossEveryLaterScan()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        Assert.True(pending.TryBegin(Signature(7, "Apple", 20, coins: 40), 0, 15, "original"));
        Assert.Single(pending.CollectNewTimeouts(15));

        for (int scan = 0; scan < 3; scan++)
        {
            int prepareCalls = 0;
            AutoSellAttemptOutcome outcome = coordinator.TryPrepareAndDispatch(
                7,
                "Apple",
                now: 30 + scan * 100,
                confirmationTimeoutSeconds: 15,
                prepare: () =>
                {
                    prepareCalls++;
                    return Attempt(10, "forbidden-retry", coins: 20);
                },
                dispatch: _ => throw new InvalidOperationException("uncertain requests are not retried"),
                out _,
                out _);

            Assert.Equal(AutoSellAttemptOutcome.BlockedByPendingRequest, outcome);
            Assert.Equal(0, prepareCalls);
        }

        Assert.True(pending.IsPending("Apple"));
    }

    [Fact]
    public void DispatchExceptionKeepsArmedRequestAndBlocksNextCandidate()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);

        Assert.Throws<InvalidOperationException>(() => coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => Attempt(20, "ambiguous-native-call", coins: 40),
            dispatch: _ => throw new InvalidOperationException("native call failed after an unknown side effect"),
            out _,
            out _));
        Assert.True(pending.IsPending("Apple"));

        int prepareCalls = 0;
        Assert.Equal(
            AutoSellAttemptOutcome.BlockedByPendingRequest,
            coordinator.TryPrepareAndDispatch(
                7,
                "Apple",
                now: 1,
                confirmationTimeoutSeconds: 15,
                prepare: () =>
                {
                    prepareCalls++;
                    return Attempt(20, "duplicate", coins: 40);
                },
                dispatch: _ => { },
                out _,
                out _));
        Assert.Equal(0, prepareCalls);
    }

    [Fact]
    public void SynchronousExactCallbackAndNormalReturnCommitsPendingRequest()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        AutoSellDispatchSignature<string> expected = Signature(7, "Apple", 20, coins: 40, medals: 2);

        AutoSellAttemptOutcome outcome = coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => Attempt(20, "first", coins: 40, medals: 2),
            dispatch: _ => Assert.True(coordinator.TryObserveCallback(expected)),
            out _,
            out PendingSale<string, string>? confirmed);

        Assert.Equal(AutoSellAttemptOutcome.Dispatched, outcome);
        Assert.False(pending.IsPending("Apple"));
        Assert.True(confirmed.HasValue);
        Assert.Equal("first", confirmed.Value.Payload);
    }

    [Fact]
    public async Task ConcurrentExactCallbackBeforeDispatchCannotConfirmNewRequest()
    {
        var pending = new AutoSellPendingTracker<PreDispatchBlockingKey, string>();
        var coordinator = new AutoSellAttemptCoordinator<PreDispatchBlockingKey, string>(pending);
        int preparationCompleted = 0;
        int dispatchEntered = 0;
        var key = new PreDispatchBlockingKey(
            () => Volatile.Read(ref preparationCompleted) == 1);

        Task<(AutoSellAttemptOutcome Outcome, PendingSale<PreDispatchBlockingKey, string>? Confirmed)> task =
            Task.Run(() =>
            {
                AutoSellAttemptOutcome outcome = coordinator.TryPrepareAndDispatch(
                    7,
                    key,
                    now: 0,
                    confirmationTimeoutSeconds: 15,
                    prepare: () =>
                    {
                        Volatile.Write(ref preparationCompleted, 1);
                        return Attempt(20, "new-request", coins: 40);
                    },
                    dispatch: _ => Volatile.Write(ref dispatchEntered, 1),
                    out _,
                    out PendingSale<PreDispatchBlockingKey, string>? confirmed);
                return (outcome, confirmed);
            });

        Assert.True(key.Blocked.Wait(TimeSpan.FromSeconds(5)));
        bool observedBeforeDispatch;
        try
        {
            Assert.Equal(0, Volatile.Read(ref dispatchEntered));
            observedBeforeDispatch = coordinator.TryObserveCallback(
                Signature(key, generation: 7, amount: 20, coins: 40));
        }
        finally
        {
            key.Release.Set();
        }

        var result = await task;
        Assert.False(observedBeforeDispatch);
        Assert.Equal(AutoSellAttemptOutcome.Dispatched, result.Outcome);
        Assert.Equal(1, Volatile.Read(ref dispatchEntered));
        Assert.Null(result.Confirmed);
        Assert.True(pending.IsPending(key));
    }

    [Fact]
    public void LaterExactCallbackCannotUnlockPendingRequest()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        AutoSellDispatchSignature<string> expected = Signature(7, "Apple", 20, coins: 40);

        Assert.Equal(
            AutoSellAttemptOutcome.Dispatched,
            coordinator.TryPrepareAndDispatch(
                7,
                "Apple",
                now: 0,
                confirmationTimeoutSeconds: 15,
                prepare: () => Attempt(20, "first", coins: 40),
                dispatch: _ => { },
                out _,
                out PendingSale<string, string>? confirmed));

        Assert.Null(confirmed);
        Assert.Single(pending.CollectNewTimeouts(15));
        Assert.True(pending.IsUncertain("Apple"));
        Assert.False(coordinator.TryObserveCallback(expected));
        Assert.True(pending.IsPending("Apple"));
        Assert.True(pending.IsUncertain("Apple"));
    }

    [Fact]
    public void ManualExactCallbackOutsideDispatchCannotUnlockPendingRequest()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        AutoSellDispatchSignature<string> expected = Signature(7, "Apple", 20, coins: 40);

        coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => Attempt(20, "first", coins: 40),
            dispatch: _ => { },
            out _,
            out _);

        Assert.False(coordinator.TryObserveCallback(expected));
        Assert.True(pending.IsPending("Apple"));
    }

    [Fact]
    public void ExactCallbackFollowedByDispatchExceptionDoesNotUnlockPendingRequest()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        AutoSellDispatchSignature<string> expected = Signature(7, "Apple", 20, coins: 40);

        Assert.Throws<InvalidOperationException>(() => coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => Attempt(20, "first", coins: 40),
            dispatch: _ =>
            {
                Assert.True(coordinator.TryObserveCallback(expected));
                throw new InvalidOperationException("native call failed after callback");
            },
            out _,
            out _));

        Assert.False(coordinator.TryObserveCallback(expected));
        Assert.True(pending.IsPending("Apple"));
    }

    [Fact]
    public void CallbackMustMatchGenerationResourceAmountAndEveryMoneyField()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);

        coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => Attempt(20, "first", coins: 40, bills: 3, medals: 2),
            dispatch: _ =>
            {
                Assert.False(coordinator.TryObserveCallback(Signature(8, "Apple", 20, 40, 3, 2)));
                Assert.False(coordinator.TryObserveCallback(Signature(7, "Milk", 20, 40, 3, 2)));
                Assert.False(coordinator.TryObserveCallback(Signature(7, "Apple", 21, 40, 3, 2)));
                Assert.False(coordinator.TryObserveCallback(Signature(7, "Apple", 20, 41, 3, 2)));
                Assert.False(coordinator.TryObserveCallback(Signature(7, "Apple", 20, 40, 4, 2)));
                Assert.False(coordinator.TryObserveCallback(Signature(7, "Apple", 20, 40, 3, 3)));
            },
            out _,
            out PendingSale<string, string>? confirmed);

        Assert.Null(confirmed);
        Assert.True(pending.IsPending("Apple"));
    }

    [Fact]
    public void NestedDispatchIsBlockedBeforeItCanPrepare()
    {
        var pending = new AutoSellPendingTracker<string, string>();
        var coordinator = new AutoSellAttemptCoordinator<string, string>(pending);
        int nestedPrepareCalls = 0;

        coordinator.TryPrepareAndDispatch(
            7,
            "Apple",
            now: 0,
            confirmationTimeoutSeconds: 15,
            prepare: () => Attempt(20, "outer", coins: 40),
            dispatch: outerAttempt =>
            {
                Assert.Equal("outer", outerAttempt.Payload);
                AutoSellAttemptOutcome nested = coordinator.TryPrepareAndDispatch(
                    7,
                    "Milk",
                    now: 0,
                    confirmationTimeoutSeconds: 15,
                    prepare: () =>
                    {
                        nestedPrepareCalls++;
                        return Attempt(10, "nested", bills: 2);
                    },
                    dispatch: _ => { },
                    out _,
                    out _);
                Assert.Equal(AutoSellAttemptOutcome.BlockedByActiveDispatch, nested);
            },
            out _,
            out _);

        Assert.Equal(0, nestedPrepareCalls);
        Assert.False(pending.IsPending("Milk"));
    }

    [Fact]
    public void NativeMoneyProjectionUsesTheNativeResultInsteadOfIntegerMultiplication()
    {
        const long firstIntegerNotExactlyRepresentableAsSingle = 16_777_217;
        var nativeRoundedResult = new AutoSellMoneySignature(16_777_216, 0, 0);
        float observedMultiplier = 0f;

        AutoSellMoneySignature projected = AutoSellNativeMoneyProjection.Project(
            1,
            multiplier =>
            {
                observedMultiplier = multiplier;
                return nativeRoundedResult;
            });

        Assert.Equal(1f, observedMultiplier);
        Assert.Equal(nativeRoundedResult, projected);
        Assert.NotEqual(
            new AutoSellMoneySignature(firstIntegerNotExactlyRepresentableAsSingle, 0, 0),
            projected);
    }

    [Fact]
    public void NativeMoneyProjectionRejectsZeroBeforeCallingTheGameOperator()
    {
        bool called = false;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoSellNativeMoneyProjection.Project(
                0,
                _ =>
                {
                    called = true;
                    return default;
                }));
        Assert.False(called);
    }

    [Fact]
    public void MoneyRangeValidationRejectsOverflowWithoutReplacingTheNativeProjection()
    {
        new AutoSellMoneySignature(16_777_217, 0, 0).ValidateMultiplicationRange(1);
        Assert.Throws<OverflowException>(() =>
            new AutoSellMoneySignature(long.MaxValue, 0, 0).ValidateMultiplicationRange(2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoSellMoneySignature(1, 0, 0).ValidateMultiplicationRange(0));
    }

    private static PreparedAutoSellAttempt<string> Attempt(
        long amount,
        string payload,
        long coins = 0,
        long bills = 0,
        long medals = 0)
    {
        return new PreparedAutoSellAttempt<string>(
            amount,
            new AutoSellMoneySignature(coins, bills, medals),
            payload);
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

    private static AutoSellDispatchSignature<PreDispatchBlockingKey> Signature(
        PreDispatchBlockingKey key,
        long generation,
        long amount,
        long coins = 0)
    {
        return new AutoSellDispatchSignature<PreDispatchBlockingKey>(
            generation,
            key,
            amount,
            new AutoSellMoneySignature(coins, 0, 0));
    }

    private sealed class PreDispatchBlockingKey
    {
        private readonly Func<bool> _shouldBlock;
        private int _blockedOnce;

        internal PreDispatchBlockingKey(Func<bool> shouldBlock)
        {
            _shouldBlock = shouldBlock;
        }

        internal ManualResetEventSlim Blocked { get; } = new ManualResetEventSlim(false);
        internal ManualResetEventSlim Release { get; } = new ManualResetEventSlim(false);

        public override int GetHashCode()
        {
            if (_shouldBlock() && Interlocked.Exchange(ref _blockedOnce, 1) == 0)
            {
                Blocked.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Pre-dispatch callback test did not release the pending tracker.");
            }

            return 7;
        }
    }
}
