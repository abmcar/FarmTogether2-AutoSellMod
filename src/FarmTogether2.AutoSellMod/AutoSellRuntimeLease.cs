using System;

namespace FarmTogether2.AutoSellMod
{
    internal sealed class AutoSellRuntimeLease<TOwner, TComponent>
        where TOwner : class
        where TComponent : class
    {
        private readonly object _sync = new object();
        private TOwner? _owner;
        private TComponent? _component;
        private bool _componentCreationInProgress;

        internal bool TryAcquire(TOwner owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            lock (_sync)
            {
                if (_owner != null)
                    return false;

                _owner = owner;
                return true;
            }
        }

        internal bool TryBeginComponentCreation(TOwner owner)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_owner, owner)
                    || _component != null
                    || _componentCreationInProgress)
                {
                    return false;
                }

                _componentCreationInProgress = true;
                return true;
            }
        }

        internal bool TryRegisterCreatingComponent(TComponent component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            lock (_sync)
            {
                if (_owner == null || !_componentCreationInProgress)
                    return false;

                if (_component == null)
                {
                    _component = component;
                    return true;
                }

                return ReferenceEquals(_component, component);
            }
        }

        internal void EndComponentCreation(TOwner owner)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_owner, owner))
                    _componentCreationInProgress = false;
            }
        }

        internal bool TryConfirmComponent(
            TOwner owner,
            TComponent component,
            out TComponent ownedComponent,
            Func<TComponent, TComponent, bool>? sameComponent = null)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            lock (_sync)
            {
                if (!ReferenceEquals(_owner, owner))
                {
                    ownedComponent = null!;
                    return false;
                }

                if (_component == null)
                    _component = component;

                bool confirmed = ReferenceEquals(_component, component)
                    || (sameComponent != null && sameComponent(_component, component));
                ownedComponent = confirmed ? _component : null!;
                return confirmed;
            }
        }

        internal bool TryCleanupAndRelease(
            TOwner owner,
            Func<TComponent, bool> cleanup)
        {
            if (cleanup == null)
                throw new ArgumentNullException(nameof(cleanup));

            lock (_sync)
            {
                if (!ReferenceEquals(_owner, owner))
                    return false;

                if (_component != null)
                {
                    bool cleanupSucceeded;
                    try
                    {
                        cleanupSucceeded = cleanup(_component);
                    }
                    catch
                    {
                        return false;
                    }

                    if (!cleanupSucceeded)
                        return false;
                }

                _componentCreationInProgress = false;
                _component = null;
                _owner = null;
                return true;
            }
        }
    }
}
