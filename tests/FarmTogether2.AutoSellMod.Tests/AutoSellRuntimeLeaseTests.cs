using System;
using System.Collections.Generic;
using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellRuntimeLeaseTests
{
    [Fact]
    public void LeaseRejectsRepeatedLoadFromSameOrDifferentOwner()
    {
        var lease = new AutoSellRuntimeLease<object, FakeComponent>();
        var firstOwner = new object();

        Assert.True(lease.TryAcquire(firstOwner));
        Assert.False(lease.TryAcquire(firstOwner));
        Assert.False(lease.TryAcquire(new object()));
    }

    [Fact]
    public void ComponentCreatedBeforeFactoryThrowsCanStillBeCleanedUp()
    {
        var lease = new AutoSellRuntimeLease<object, FakeComponent>();
        var owner = new object();
        var component = new FakeComponent();
        Assert.True(lease.TryAcquire(owner));
        Assert.True(lease.TryBeginComponentCreation(owner));

        Assert.True(lease.TryRegisterCreatingComponent(component));
        lease.EndComponentCreation(owner);

        Assert.True(lease.TryCleanupAndRelease(owner, value => value.Cleanup()));
        Assert.Equal(new[] { "inactive", "detached", "cleared", "destroyed" }, component.Events);
        Assert.True(lease.TryAcquire(new object()));
    }

    [Fact]
    public void SuccessfulCreationConfirmsTheAwakeRegisteredComponent()
    {
        var lease = new AutoSellRuntimeLease<object, FakeComponent>();
        var owner = new object();
        var component = new FakeComponent();
        Assert.True(lease.TryAcquire(owner));
        Assert.True(lease.TryBeginComponentCreation(owner));
        Assert.True(lease.TryRegisterCreatingComponent(component));
        lease.EndComponentCreation(owner);

        Assert.True(lease.TryConfirmComponent(owner, component, out FakeComponent owned));
        Assert.Same(component, owned);
        Assert.False(lease.TryConfirmComponent(owner, new FakeComponent(), out _));
    }

    [Fact]
    public void EquivalentFactoryWrapperKeepsTheAwakeRegisteredComponentAsOwner()
    {
        var lease = new AutoSellRuntimeLease<object, FakeComponent>();
        var owner = new object();
        var awakeWrapper = new FakeComponent(nativeId: 7);
        var factoryWrapper = new FakeComponent(nativeId: 7);
        Assert.True(lease.TryAcquire(owner));
        Assert.True(lease.TryBeginComponentCreation(owner));
        Assert.True(lease.TryRegisterCreatingComponent(awakeWrapper));
        lease.EndComponentCreation(owner);

        Assert.True(lease.TryConfirmComponent(
            owner,
            factoryWrapper,
            out FakeComponent owned,
            static (registered, returned) => registered.NativeId == returned.NativeId));
        Assert.Same(awakeWrapper, owned);

        Assert.True(lease.TryCleanupAndRelease(owner, value => value.Cleanup()));
        Assert.Equal(new[] { "inactive", "detached", "cleared", "destroyed" }, awakeWrapper.Events);
        Assert.Empty(factoryWrapper.Events);
    }

    [Fact]
    public void FailedCleanupRetainsLeaseUntilCleanupSucceeds()
    {
        var lease = new AutoSellRuntimeLease<object, FakeComponent>();
        var owner = new object();
        var component = new FakeComponent { FailCleanup = true };
        Assert.True(lease.TryAcquire(owner));
        Assert.True(lease.TryBeginComponentCreation(owner));
        Assert.True(lease.TryRegisterCreatingComponent(component));
        lease.EndComponentCreation(owner);

        Assert.False(lease.TryCleanupAndRelease(owner, value => value.Cleanup()));
        Assert.False(lease.TryAcquire(new object()));
        Assert.False(component.RuntimeGate.CanRun);
        Assert.False(component.RuntimeGate.CanProcessTownActions);

        component.FailCleanup = false;
        Assert.True(lease.TryCleanupAndRelease(owner, value => value.Cleanup()));
        Assert.True(lease.TryAcquire(new object()));
    }

    [Fact]
    public void OwnerWithoutComponentCanReleaseAfterCompatibilityFailure()
    {
        var lease = new AutoSellRuntimeLease<object, FakeComponent>();
        var owner = new object();
        Assert.True(lease.TryAcquire(owner));

        Assert.True(lease.TryCleanupAndRelease(
            owner,
            _ => throw new InvalidOperationException("no component should be cleaned")));
        Assert.True(lease.TryAcquire(new object()));
    }

    private sealed class FakeComponent
    {
        internal FakeComponent(int nativeId = 0)
        {
            NativeId = nativeId;
            RuntimeGate.Activate();
            RuntimeGate.EnableTownActionCallbacks();
        }

        internal List<string> Events { get; } = new List<string>();
        internal int NativeId { get; }
        internal bool FailCleanup { get; set; }
        internal AutoSellRuntimeGate RuntimeGate { get; } = new AutoSellRuntimeGate();

        internal bool Cleanup()
        {
            RuntimeGate.Deactivate();
            Events.Add("inactive");
            if (FailCleanup)
                return false;

            Events.Add("detached");
            Events.Add("cleared");
            Events.Add("destroyed");
            return true;
        }
    }
}
