using System;
using System.Globalization;

namespace MacExtend.Core
{
    /// <summary>Fixed 4-digit PIN that uxplay asks the Mac for on first pairing (uxplay accepts 0001..9999).</summary>
    public static class Pin
    {
        public static string Generate(Random rng) => rng.Next(1000, 10000).ToString(CultureInfo.InvariantCulture);

        public static bool IsValid(string pin)
        {
            if (pin == null || pin.Length != 4 || pin == "0000") return false;
            foreach (var c in pin)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
