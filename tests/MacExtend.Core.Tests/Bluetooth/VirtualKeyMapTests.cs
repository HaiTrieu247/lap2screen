using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class VirtualKeyMapTests
{
    [Theory]
    [InlineData(0x41, 0x04)] // A
    [InlineData(0x5A, 0x1D)] // Z
    [InlineData(0x31, 0x1E)] // 1
    [InlineData(0x30, 0x27)] // 0
    [InlineData(0x70, 0x3A)] // F1
    [InlineData(0x7B, 0x45)] // F12
    [InlineData(0x0D, 0x28)] // Enter
    [InlineData(0x1B, 0x29)] // Escape
    [InlineData(0x08, 0x2A)] // Backspace
    [InlineData(0x20, 0x2C)] // Space
    [InlineData(0xBF, 0x38)] // /
    [InlineData(0x25, 0x50)] // Left
    [InlineData(0x2E, 0x4C)] // Delete
    [InlineData(0x5D, 0x65)] // Application (menu) key
    public void MapsVirtualKeysToHidUsages(int virtualKey, int usage)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(virtualKey, 0, false, out var actual));
        Assert.Equal(usage, actual);
    }

    [Fact]
    public void KeypadEnterIsToldApartByTheExtendedFlag()
    {
        Assert.True(VirtualKeyMap.TryGetUsage(0x0D, 0x1C, true, out var usage));
        Assert.Equal(0x58, usage);
    }

    [Fact]
    public void NumpadKeysFollowThePhysicalKeyNotLocalNumLock()
    {
        // with Num Lock off Windows reports Keypad 7 as Home
        Assert.True(VirtualKeyMap.TryGetUsage(0x24, 0x47, false, out var keypad));
        Assert.Equal(0x5F, keypad);

        // the real Home key shares the scan code but is extended
        Assert.True(VirtualKeyMap.TryGetUsage(0x24, 0x47, true, out var home));
        Assert.Equal(0x4A, home);
    }

    [Theory]
    [InlineData(0xAF)] // volume up
    [InlineData(0xAD)] // mute
    [InlineData(0xB3)] // play/pause
    [InlineData(0xA0)] // left shift: a modifier bit, not a key usage
    [InlineData(0x5B)] // left Windows: a modifier bit
    public void KeysWithoutAUsageAreReported(int virtualKey)
    {
        Assert.False(VirtualKeyMap.TryGetUsage(virtualKey, 0, false, out _));
    }

    [Theory]
    [InlineData(0xA0, KeyModifiers.LeftShift)]
    [InlineData(0xA1, KeyModifiers.RightShift)]
    [InlineData(0xA2, KeyModifiers.LeftControl)]
    [InlineData(0xA3, KeyModifiers.RightControl)]
    [InlineData(0xA4, KeyModifiers.LeftAlt)]
    [InlineData(0xA5, KeyModifiers.RightAlt)]
    [InlineData(0x5B, KeyModifiers.LeftGui)]
    [InlineData(0x5C, KeyModifiers.RightGui)]
    public void ModifierKeysMapToModifierBits(int virtualKey, KeyModifiers modifier)
    {
        Assert.Equal(modifier, VirtualKeyMap.ModifierOf(virtualKey));
    }

    [Fact]
    public void OrdinaryKeysAreNotModifiers()
    {
        Assert.Equal(KeyModifiers.None, VirtualKeyMap.ModifierOf(0x41));
    }
}
