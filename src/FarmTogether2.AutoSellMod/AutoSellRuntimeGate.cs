using System;

namespace FarmTogether2.AutoSellMod
{
    internal sealed class AutoSellRuntimeGate
    {
        internal bool CanRun { get; private set; }
        internal bool CanProcessTownActions { get; private set; }

        internal void Activate()
        {
            if (CanRun)
                throw new InvalidOperationException("AutoSell runtime is already active.");

            CanRun = true;
            CanProcessTownActions = false;
        }

        internal void EnableTownActionCallbacks()
        {
            if (!CanRun)
                throw new InvalidOperationException("Inactive AutoSell runtime cannot accept callbacks.");

            CanProcessTownActions = true;
        }

        internal void DisableTownActionCallbacks()
        {
            CanProcessTownActions = false;
        }

        internal void Deactivate()
        {
            CanProcessTownActions = false;
            CanRun = false;
        }
    }
}
