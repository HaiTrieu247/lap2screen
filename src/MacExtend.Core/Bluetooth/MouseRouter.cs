namespace MacExtend.Core.Bluetooth
{
    /// <summary>
    /// Mouse buttons and wheel while the laptop controls the device. A button Windows saw go down keeps going to
    /// Windows until it is released; motion is handled by the caller as a delta from the parked cursor.
    /// </summary>
    public sealed class MouseRouter
    {
        private const int WheelNotch = 120;

        private MouseButtons _windows;
        private int _wheel;

        /// <param name="heldByWindows">Buttons already down when control starts.</param>
        public MouseRouter(MouseButtons heldByWindows = MouseButtons.None)
        {
            _windows = heldByWindows;
        }

        /// <summary>Buttons held on the device.</summary>
        public MouseButtons Buttons { get; private set; }

        /// <returns>true to keep the event from Windows and send <see cref="Buttons"/> to the device.</returns>
        public bool OnButton(MouseButtons button, bool down)
        {
            if ((_windows & button) != 0)
            {
                if (!down) _windows &= ~button;
                return false;
            }
            if (!down && (Buttons & button) == 0) return false;
            Buttons = down ? Buttons | button : Buttons & ~button;
            return true;
        }

        /// <summary>Adds a WM_MOUSEWHEEL delta; smooth touchpad scrolling arrives in fractions of a notch.</summary>
        /// <returns>Whole notches to send now, positive away from the user.</returns>
        public int OnWheel(int delta)
        {
            _wheel += delta;
            var notches = _wheel / WheelNotch;
            _wheel -= notches * WheelNotch;
            return notches;
        }

        /// <returns>true if a button was held, so the device needs an all-up report.</returns>
        public bool ReleaseAll()
        {
            var held = Buttons != MouseButtons.None;
            Buttons = MouseButtons.None;
            _wheel = 0;
            return held;
        }
    }
}
