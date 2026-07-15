using System;
using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellRuntimeGateTests
{
    [Fact]
    public void GateIsInactiveUntilLoadActivatesIt()
    {
        var gate = new AutoSellRuntimeGate();

        Assert.False(gate.CanRun);
        Assert.False(gate.CanProcessTownActions);
        Assert.Throws<InvalidOperationException>(() => gate.EnableTownActionCallbacks());
    }

    [Fact]
    public void FarmSessionEnablesCallbacksOnlyWhileRuntimeIsActive()
    {
        var gate = new AutoSellRuntimeGate();
        gate.Activate();
        Assert.True(gate.CanRun);
        Assert.False(gate.CanProcessTownActions);

        gate.EnableTownActionCallbacks();
        Assert.True(gate.CanProcessTownActions);

        gate.DisableTownActionCallbacks();
        Assert.True(gate.CanRun);
        Assert.False(gate.CanProcessTownActions);
    }

    [Fact]
    public void FailedDetachAfterDeactivationCannotReactivateAStaleCallback()
    {
        var gate = new AutoSellRuntimeGate();
        gate.Activate();
        gate.EnableTownActionCallbacks();

        gate.Deactivate();

        Assert.False(gate.CanRun);
        Assert.False(gate.CanProcessTownActions);
        Assert.Throws<InvalidOperationException>(() => gate.EnableTownActionCallbacks());
    }
}
