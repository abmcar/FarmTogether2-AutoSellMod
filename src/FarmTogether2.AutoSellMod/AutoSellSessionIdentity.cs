using System;

namespace FarmTogether2.AutoSellMod
{
    internal enum AutoSellSessionReadStatus
    {
        NoActiveSession,
        IdentityUnavailable,
        Available,
    }

    internal readonly struct AutoSellSessionIdentity : IEquatable<AutoSellSessionIdentity>
    {
        private readonly IntPtr _stagePointer;
        private readonly int _stageInstanceId;
        private readonly IntPtr _farmPointer;
        private readonly IntPtr _playerPointer;
        private readonly int _playerInstanceId;

        internal AutoSellSessionIdentity(
            IntPtr stagePointer,
            int stageInstanceId,
            IntPtr farmPointer,
            IntPtr playerPointer,
            int playerInstanceId)
        {
            if (stagePointer == IntPtr.Zero)
                throw new ArgumentOutOfRangeException(nameof(stagePointer));
            _stagePointer = stagePointer;
            _stageInstanceId = stageInstanceId;
            if (farmPointer == IntPtr.Zero)
                throw new ArgumentOutOfRangeException(nameof(farmPointer));
            _farmPointer = farmPointer;
            if (playerPointer == IntPtr.Zero)
                throw new ArgumentOutOfRangeException(nameof(playerPointer));
            _playerPointer = playerPointer;
            _playerInstanceId = playerInstanceId;
        }

        public bool Equals(AutoSellSessionIdentity other)
        {
            return _stagePointer == other._stagePointer
                && _stageInstanceId == other._stageInstanceId
                && _farmPointer == other._farmPointer
                && _playerPointer == other._playerPointer
                && _playerInstanceId == other._playerInstanceId;
        }

        public override bool Equals(object? obj)
        {
            return obj is AutoSellSessionIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                _stagePointer,
                _stageInstanceId,
                _farmPointer,
                _playerPointer,
                _playerInstanceId);
        }
    }
}
