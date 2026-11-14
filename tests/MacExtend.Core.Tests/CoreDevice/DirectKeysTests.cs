using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class DirectKeysTests
{
    [Theory]
    [InlineData(0x41, 0x1Eu, false, 0x04)]   // A
    [InlineData(0x0D, 0x1Cu, false, 0x28)]   // Enter
    [InlineData(0x0D, 0x1Cu, true, 0x58)]    // keypad Enter
    [InlineData(0x08, 0x0Eu, false, 0x2A)]   // Backspace
    [InlineData(0x26, 0x48u, true, 0x52)]    // Up arrow, not keypad 8
    public void OrdinaryKeysAreTheKeyboardUsage(int vk, uint scan, bool extended, int usage)
    {
        Assert.True(DirectKeys.TryGetUsage(vk, scan, extended, out var u));
        Assert.Equal(usage, u);
    }

    [Theory]
    // a window gets the generic Shift, Ctrl and Alt: the scan code or the extended flag says which side
    [InlineData(0x10, 0x2Au, false, 0xE1)]   // left Shift
    [InlineData(0x10, 0x36u, false, 0xE5)]   // right Shift
    [InlineData(0x11, 0x1Du, false, 0xE3)]   // left Ctrl: iOS's Command, so Ctrl+C, Ctrl+V, Ctrl+Z do what they do on Windows
    [InlineData(0x11, 0x1Du, true, 0xE7)]    // right Ctrl
    [InlineData(0x12, 0x38u, false, 0xE2)]   // left Alt: Option
    [InlineData(0x12, 0x38u, true, 0xE6)]    // right Alt
    [InlineData(0xA2, 0x1Du, false, 0xE3)]   // the left/right codes a hook would give
    [InlineData(0xA5, 0x38u, true, 0xE6)]
    public void ModifiersAreTheirOwnUsages(int vk, uint scan, bool extended, int usage)
    {
        Assert.True(DirectKeys.TryGetUsage(vk, scan, extended, out var u));
        Assert.Equal(usage, u);
    }

    [Theory]
    [InlineData(0xE7, 0u, false)]   // VK_PACKET: a character an IME or UniKey made up, no key behind it
    [InlineData(0xAD, 0u, false)]   // volume mute
    [InlineData(0x5B, 0x5Bu, true)] // Windows key: Windows keeps it for the Start menu
    public void KeysWithoutAUsageAreNotSent(int vk, uint scan, bool extended)
    {
        Assert.False(DirectKeys.TryGetUsage(vk, scan, extended, out _));
    }
}
