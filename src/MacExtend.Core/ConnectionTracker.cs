namespace MacExtend.Core
{
    public enum ReceiverPhase
    {
        Stopped,
        Starting,
        Ready,
        Connected,
        Failed,
    }

    /// <summary>
    /// Decides whether a Mac is currently using the display. uxplay logs the start of a session at
    /// INFO level but the end only at DEBUG level, so the end is detected from TCP connections
    /// owned by the uxplay process dropping to zero. Closing the video window is logged, so that
    /// ends the session at once; the TCP check still covers a log line that arrives late.
    /// </summary>
    public sealed class ConnectionTracker
    {
        private const int ZeroSamplesToDisconnect = 2;
        private int _zeroSamples;

        public string ClientName { get; private set; }
        /// <summary>The model uxplay reported with the name ("iPhone12,1", "MacBookPro18,3"); null while nothing is connected.</summary>
        public string ClientModel { get; private set; }
        public bool IsConnected { get; private set; }
        /// <summary>The mirrored picture's size uxplay last logged, in pixels; 0 until then and after the session ends.</summary>
        public int PictureWidth { get; private set; }
        public int PictureHeight { get; private set; }

        /// <returns>true if <see cref="IsConnected"/> or <see cref="ClientName"/> changed.</returns>
        public bool OnLog(LogEvent e)
        {
            switch (e.Kind)
            {
                case LogEventKind.ClientConnecting:
                    var changed = !IsConnected || ClientName != e.ClientName || ClientModel != e.ClientModel;
                    IsConnected = true;
                    ClientName = e.ClientName;
                    ClientModel = e.ClientModel;
                    // the size of the previous client's picture says nothing about this one's
                    PictureWidth = 0;
                    PictureHeight = 0;
                    _zeroSamples = 0;
                    return changed;
                case LogEventKind.ConnectionLost:
                case LogEventKind.VideoWindowClosed:
                    return Disconnect();
                default:
                    return false;
            }
        }

        /// <returns>true if the connection ended.</returns>
        public bool OnTcpSample(int established)
        {
            if (!IsConnected) return false;
            if (established > 0)
            {
                _zeroSamples = 0;
                return false;
            }
            return ++_zeroSamples >= ZeroSamplesToDisconnect && Disconnect();
        }

        /// <returns>true if <paramref name="e"/> is a picture size that differs from the one known.</returns>
        public bool OnPictureSize(LogEvent e)
        {
            if (e == null || e.Kind != LogEventKind.PictureSize) return false;
            if (e.PictureWidth == PictureWidth && e.PictureHeight == PictureHeight) return false;
            PictureWidth = e.PictureWidth;
            PictureHeight = e.PictureHeight;
            return true;
        }

        public void Reset() => Disconnect();

        private bool Disconnect()
        {
            var changed = IsConnected;
            IsConnected = false;
            ClientName = null;
            ClientModel = null;
            PictureWidth = 0;
            PictureHeight = 0;
            _zeroSamples = 0;
            return changed;
        }
    }
}
