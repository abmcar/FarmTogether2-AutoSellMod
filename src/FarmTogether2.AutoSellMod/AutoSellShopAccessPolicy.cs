using System;

namespace FarmTogether2.AutoSellMod
{
    internal static class AutoSellShopAccessPolicy
    {
        internal static bool CanScanShop(
            bool hasFullPermissions,
            Func<bool> nativeOpenCheck,
            Action<Exception> onFailure)
        {
            if (hasFullPermissions)
                return true;

            try
            {
                return nativeOpenCheck();
            }
            catch (Exception exception)
            {
                onFailure(exception);
                return false;
            }
        }
    }
}
