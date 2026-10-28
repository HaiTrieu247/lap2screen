using System;

namespace MacExtend.Core.Bluetooth
{
    /// <summary>A device that paired with the laptop and subscribed to its keyboard or mouse reports.</summary>
    public sealed class PairedHost : IEquatable<PairedHost>
    {
        public PairedHost(string id, string name)
        {
            Id = id ?? "";
            Name = name ?? "";
        }

        /// <summary>WinRT device id, e.g. "BluetoothLE#BluetoothLE&lt;adapter&gt;-&lt;device address&gt;".</summary>
        public string Id { get; }
        public string Name { get; }

        /// <summary>The device name, or its Bluetooth address when it has not told us one.</summary>
        public string Display
        {
            get
            {
                if (Name.Length > 0) return Name;
                var dash = Id.LastIndexOf('-');
                return dash >= 0 && dash < Id.Length - 1 ? Id.Substring(dash + 1) : Id;
            }
        }

        public bool Equals(PairedHost other) => other != null && Id == other.Id && Name == other.Name;

        public override bool Equals(object obj) => Equals(obj as PairedHost);

        public override int GetHashCode() => Id.GetHashCode() * 31 + Name.GetHashCode();

        public override string ToString() => Display;
    }
}
