using System;
using System.Collections.Generic;
using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class AutoSellShopAccessPolicyTests
{
    [Fact]
    public void FullPermissionsBypassThrowingNativeCheck()
    {
        bool nativeCheckCalled = false;
        var failures = new List<Exception>();

        bool result = AutoSellShopAccessPolicy.CanScanShop(
            hasFullPermissions: true,
            nativeOpenCheck: () =>
            {
                nativeCheckCalled = true;
                throw new NullReferenceException("closedTownShops");
            },
            onFailure: failures.Add);

        Assert.True(result);
        Assert.False(nativeCheckCalled);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonFullPermissionsPreserveNativeResult(bool nativeResult)
    {
        var failures = new List<Exception>();

        bool result = AutoSellShopAccessPolicy.CanScanShop(
            hasFullPermissions: false,
            nativeOpenCheck: () => nativeResult,
            onFailure: failures.Add);

        Assert.Equal(nativeResult, result);
        Assert.Empty(failures);
    }

    [Fact]
    public void NonFullNativeExceptionFailsClosedAndReportsOnce()
    {
        var expected = new NullReferenceException("closedTownShops");
        var failures = new List<Exception>();

        bool result = AutoSellShopAccessPolicy.CanScanShop(
            hasFullPermissions: false,
            nativeOpenCheck: () => throw expected,
            onFailure: failures.Add);

        Assert.False(result);
        Assert.Equal(new[] { expected }, failures);
    }
}
