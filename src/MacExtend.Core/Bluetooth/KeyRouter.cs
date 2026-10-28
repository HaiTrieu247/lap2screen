using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.Bluetooth
{
    public enum KeyAction
    {
        /// <summary>Keep the key from Windows and send <see cref="KeyRouter.Report"/> to the device.</summary>
        Forward,
        /// <summary>Let Windows have the key.</summary>
        PassThrough,
        /// <summary>Ctrl+Alt+Q: keep it from Windows, send the all-up <see cref="KeyRouter.Report"/>, end control.</summary>
        Stop,
        /// <summary>
        /// A shortcut that belongs to Windows (Alt+Tab, Alt+Enter): send the all-up report, end control and press
        /// <see cref="KeyRouter.HandOver"/> on Windows so it sees the whole shortcut.
        /// </summary>
        HandToWindows,
    }

    /// <summary>
    /// Decides, key event by key event, what goes to the device while the laptop controls it. A key whose press
    /// Windows saw (held when control started, or one the device has no usage for, like volume) stays with
    /// Windows until it is released, so neither side is left with a key stuck down.
    /// </summary>
    public sealed class KeyRouter
    {
        private const int Tab = 0x09, Enter = 0x0D, Q = 0x51;

        private readonly HashSet<int> _windows;
        // keys pressed on the device, in press order
        private readonly List<int> _forwarded = new List<int>();
        // the usage each key was sent as: the numpad usage depends on the scan code of the press
        private readonly Dictionary<int, byte> _usageOf = new Dictionary<int, byte>();
        private readonly List<byte> _usages = new List<byte>();
        private KeyModifiers _modifiers;

        public KeyRouter() : this(new int[0])
        {
        }

        /// <param name="heldByWindows">Left/right-specific virtual keys already down when control starts.</param>
        public KeyRouter(IEnumerable<int> heldByWindows)
        {
            _windows = new HashSet<int>(heldByWindows);
            Report = HidReports.KeyboardRelease();
        }

        /// <summary>The keyboard report after the last event: what the device should consider held.</summary>
        public byte[] Report { get; private set; }

        /// <summary>After <see cref="KeyAction.HandToWindows"/>: virtual keys to press on Windows, modifiers first.</summary>
        public IReadOnlyList<int> HandOver { get; private set; } = new int[0];

        public KeyAction OnKey(int virtualKey, bool down, uint scanCode = 0, bool extended = false)
        {
            if (!down) return OnRelease(virtualKey);
            // auto-repeat of a key Windows owns
            if (_windows.Contains(virtualKey)) return KeyAction.PassThrough;

            if (virtualKey == Q && IsHeld(IsControl) && IsHeld(IsAlt))
            {
                ReleaseAll();
                return KeyAction.Stop;
            }
            if ((virtualKey == Tab || virtualKey == Enter) && IsHeld(IsAlt))
            {
                var handOver = _forwarded.Where(k => VirtualKeyMap.ModifierOf(k) != KeyModifiers.None).ToList();
                handOver.Add(virtualKey);
                ReleaseAll();
                foreach (var key in handOver) _windows.Add(key);
                HandOver = handOver;
                return KeyAction.HandToWindows;
            }

            var modifier = VirtualKeyMap.ModifierOf(virtualKey);
            if (modifier != KeyModifiers.None)
            {
                if (!_forwarded.Contains(virtualKey)) _forwarded.Add(virtualKey);
                _modifiers |= modifier;
                Update();
                return KeyAction.Forward;
            }
            if (VirtualKeyMap.TryGetUsage(virtualKey, scanCode, extended, out var usage))
            {
                if (!_forwarded.Contains(virtualKey))
                {
                    _forwarded.Add(virtualKey);
                    _usageOf[virtualKey] = usage;
                    if (!_usages.Contains(usage)) _usages.Add(usage);
                }
                Update();
                return KeyAction.Forward;
            }

            _windows.Add(virtualKey);
            return KeyAction.PassThrough;
        }

        private KeyAction OnRelease(int virtualKey)
        {
            if (_windows.Remove(virtualKey) || !_forwarded.Remove(virtualKey)) return KeyAction.PassThrough;

            var modifier = VirtualKeyMap.ModifierOf(virtualKey);
            if (modifier != KeyModifiers.None)
            {
                _modifiers &= ~modifier;
            }
            else if (_usageOf.TryGetValue(virtualKey, out var usage))
            {
                _usageOf.Remove(virtualKey);
                if (!_usageOf.ContainsValue(usage)) _usages.Remove(usage);
            }
            Update();
            return KeyAction.Forward;
        }

        private bool IsHeld(System.Func<int, bool> kind) => _forwarded.Any(kind) || _windows.Any(kind);

        private static bool IsControl(int virtualKey) => virtualKey == 0xA2 || virtualKey == 0xA3 || virtualKey == 0x11;

        private static bool IsAlt(int virtualKey) => virtualKey == 0xA4 || virtualKey == 0xA5 || virtualKey == 0x12;

        private void ReleaseAll()
        {
            _forwarded.Clear();
            _usageOf.Clear();
            _usages.Clear();
            _modifiers = KeyModifiers.None;
            Update();
        }

        private void Update() => Report = HidReports.Keyboard(_modifiers, _usages);
    }
}
