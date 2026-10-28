using System;

namespace MacExtend.Core.Bluetooth
{
    /// <summary>
    /// When the laptop is a Bluetooth keyboard and mouse: only while a device mirrors, because iOS hides its
    /// on-screen keyboard for as long as a hardware one is connected. It stays up for <see cref="Linger"/> after
    /// mirroring ends, so a session that drops and comes straight back does not wait for Bluetooth to reconnect.
    /// </summary>
    public sealed class BluetoothLifetime
    {
        public static readonly TimeSpan Linger = TimeSpan.FromSeconds(10);

        private readonly Func<DateTime> _clock;
        private DateTime? _mirroringEnded;
        private bool _running;

        public BluetoothLifetime(Func<DateTime> clock)
        {
            _clock = clock;
        }

        public bool ShouldRun(bool enabled, bool mirroring)
        {
            if (!enabled)
            {
                _running = false;
                _mirroringEnded = null;
            }
            else if (mirroring)
            {
                _running = true;
                _mirroringEnded = null;
            }
            else if (_running)
            {
                var now = _clock();
                if (_mirroringEnded == null) _mirroringEnded = now;
                if (now - _mirroringEnded.Value >= Linger)
                {
                    _running = false;
                    _mirroringEnded = null;
                }
            }
            return _running;
        }
    }
}
