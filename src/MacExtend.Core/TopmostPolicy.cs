namespace MacExtend.Core
{
    public enum TopmostAction
    {
        Keep,
        Set,
        Clear,
    }

    /// <summary>
    /// Whether MacExtend should change the "always on top" flag of uxplay's video window. Only a normal or
    /// maximized window is pinned: a pinned full-screen window would stay over the whole monitor after
    /// Alt+Tab. A flag MacExtend did not set is never cleared, so a topmost state uxplay chose is left alone.
    /// </summary>
    public static class TopmostPolicy
    {
        /// <param name="alwaysOnTop">The user's setting.</param>
        /// <param name="topmost">The window's current topmost flag.</param>
        /// <param name="pinnedByUs">MacExtend set the flag on this window and has not cleared it since.</param>
        public static TopmostAction Decide(bool alwaysOnTop, bool minimized, bool fullscreen, bool topmost, bool pinnedByUs)
        {
            if (alwaysOnTop && !fullscreen)
            {
                // a minimized window is pinned once it is shown again
                return topmost || minimized ? TopmostAction.Keep : TopmostAction.Set;
            }
            return topmost && pinnedByUs ? TopmostAction.Clear : TopmostAction.Keep;
        }
    }
}
