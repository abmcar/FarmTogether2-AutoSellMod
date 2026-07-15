using System;
using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellSessionIdentityTests
{
    [Fact]
    public void SameNativeAndUnityIdentityMatchesAcrossWrapperReads()
    {
        var firstRead = new AutoSellSessionIdentity(
            new IntPtr(50),
            stageInstanceId: 500,
            new IntPtr(100),
            new IntPtr(200),
            playerInstanceId: 600);
        var laterRead = new AutoSellSessionIdentity(
            new IntPtr(50),
            stageInstanceId: 500,
            new IntPtr(100),
            new IntPtr(200),
            playerInstanceId: 600);

        Assert.Equal(firstRead, laterRead);
    }

    [Fact]
    public void NativePointerChangesDoNotMatch()
    {
        AutoSellSessionIdentity identity = Identity();

        Assert.NotEqual(identity, Identity(stagePointer: 51));
        Assert.NotEqual(identity, Identity(farmPointer: 101));
        Assert.NotEqual(identity, Identity(playerPointer: 201));
    }

    [Fact]
    public void UnityInstanceIdChangesDoNotMatchEvenWhenPointersAreReused()
    {
        AutoSellSessionIdentity identity = Identity();

        Assert.NotEqual(identity, Identity(stageInstanceId: 501));
        Assert.NotEqual(identity, Identity(playerInstanceId: 601));
    }

    [Fact]
    public void InvalidNativePointersCannotCreateASessionIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Identity(stagePointer: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Identity(farmPointer: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Identity(playerPointer: 0));
    }

    private static AutoSellSessionIdentity Identity(
        long stagePointer = 50,
        int stageInstanceId = 500,
        long farmPointer = 100,
        long playerPointer = 200,
        int playerInstanceId = 600)
    {
        return new AutoSellSessionIdentity(
            new IntPtr(stagePointer),
            stageInstanceId,
            new IntPtr(farmPointer),
            new IntPtr(playerPointer),
            playerInstanceId);
    }
}
