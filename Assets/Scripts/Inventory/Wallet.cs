using System;
using Davidmon.Core;

namespace Davidmon.Inventory
{
    /// <summary>
    /// Tracks the player's coins. All currency changes flow through this service and
    /// are broadcast on <see cref="GameEvents"/>, so the HUD and shop listen to the
    /// event rather than polling fields. Intentionally small and swappable — milestone
    /// 14 will replace the in-memory store with a persistent backend.
    /// </summary>
    public sealed class Wallet
    {
        private long _coins;

        public long Coins => _coins;

        /// <summary>Give coins. Returns the resulting total.</summary>
        public long AddCoins(long amount)
        {
            if (amount <= 0) return _coins;
            long old = _coins;
            _coins += amount;
            GameEvents.RaiseCurrencyChanged(old, _coins);
            return _coins;
        }

        /// <summary>Spend coins if affordable. Returns whether the charge went through.</summary>
        public bool TrySpend(long amount)
        {
            if (amount <= 0) return true;
            if (_coins < amount) return false;
            long old = _coins;
            _coins -= amount;
            GameEvents.RaiseCurrencyChanged(old, _coins);
            return true;
        }

        /// <summary>Restores the balance directly (save/load). Raises the currency channel.</summary>
        public void SetCoins(long amount)
        {
            long old = _coins;
            _coins = Math.Max(0L, amount);
            GameEvents.RaiseCurrencyChanged(old, _coins);
        }
    }
}