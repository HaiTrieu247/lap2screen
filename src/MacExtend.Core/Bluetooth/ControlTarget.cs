using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.Bluetooth
{
    /// <summary>
    /// The Bluetooth host to control. Nothing links a Bluetooth connection to an AirPlay one, so it is guessed:
    /// the host named like the device mirroring, else the only host connected. A host the user chose instead is
    /// tied to the AirPlay source it was chosen for: when another device starts mirroring the choice is dropped,
    /// the same device reconnecting keeps it.
    /// </summary>
    public sealed class ControlTarget
    {
        private string _source;
        private bool _chosen;

        public string HostId { get; private set; }

        /// <summary>Re-evaluates the guess unless the user's choice still stands.</summary>
        /// <param name="connected">Hosts connected over Bluetooth now.</param>
        /// <param name="airPlaySource">Name of the device mirroring now, or null.</param>
        public void Resolve(IReadOnlyList<PairedHost> connected, string airPlaySource)
        {
            OnAirPlaySource(airPlaySource);
            if (_chosen) return;

            var source = (airPlaySource ?? "").Trim();
            var named = connected.FirstOrDefault(h => source.Length > 0 && string.Equals(h.Name.Trim(), source, StringComparison.OrdinalIgnoreCase));
            HostId = named?.Id ?? (connected.Count == 1 ? connected[0].Id : null);
        }

        /// <param name="airPlaySource">Name of the device mirroring now, or null: then the next one to connect.</param>
        public void Select(string hostId, string airPlaySource)
        {
            HostId = hostId;
            _chosen = hostId != null;
            _source = string.IsNullOrEmpty(airPlaySource) ? null : airPlaySource;
        }

        public void Clear()
        {
            HostId = null;
            _chosen = false;
            _source = null;
        }

        /// <param name="source">Name of the device mirroring now, null while none is.</param>
        /// <returns>true if this dropped the target.</returns>
        public bool OnAirPlaySource(string source)
        {
            if (HostId == null || string.IsNullOrEmpty(source)) return false;
            if (_source == null)
            {
                _source = source;
                return false;
            }
            if (string.Equals(_source, source, StringComparison.Ordinal)) return false;
            Clear();
            return true;
        }
    }

    public enum ControlBlocker
    {
        None,
        /// <summary>Bluetooth control is switched off.</summary>
        Off,
        /// <summary>The laptop is not a Bluetooth keyboard right now: starting, unsupported, radio off or failed.</summary>
        BluetoothNotReady,
        NoTarget,
        /// <summary>The chosen host is not connected over Bluetooth right now.</summary>
        TargetDisconnected,
        NoAirPlay,
        NoVideo,
    }

    public static class ControlReadiness
    {
        /// <summary>The first thing missing before the laptop can start controlling the device.</summary>
        public static ControlBlocker Evaluate(bool enabled, bool bluetoothReady, IEnumerable<string> connectedHostIds,
                                              string selectedHostId, bool airPlayConnected, bool videoWindow)
        {
            if (!enabled) return ControlBlocker.Off;
            if (!bluetoothReady) return ControlBlocker.BluetoothNotReady;
            if (selectedHostId == null) return ControlBlocker.NoTarget;
            if (!connectedHostIds.Contains(selectedHostId, StringComparer.OrdinalIgnoreCase)) return ControlBlocker.TargetDisconnected;
            if (!airPlayConnected) return ControlBlocker.NoAirPlay;
            return videoWindow ? ControlBlocker.None : ControlBlocker.NoVideo;
        }
    }
}
