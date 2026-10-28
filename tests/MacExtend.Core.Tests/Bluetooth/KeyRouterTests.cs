using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class KeyRouterTests
{
    private const int A = 0x41, B = 0x42, Q = 0x51, Tab = 0x09, Enter = 0x0D, VolumeUp = 0xAF;
    private const int LShift = 0xA0, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5, LWin = 0x5B;

    private static readonly byte[] Released = new byte[HidReports.KeyboardReportLength];

    [Fact]
    public void LetterIsForwardedDownAndUp()
    {
        var router = new KeyRouter();

        Assert.Equal(KeyAction.Forward, router.OnKey(A, true));
        Assert.Equal(new byte[] { 0, 0, 0x04, 0, 0, 0, 0, 0 }, router.Report);
        Assert.Equal(KeyAction.Forward, router.OnKey(A, false));
        Assert.Equal(Released, router.Report);
    }

    [Fact]
    public void ModifiersGoIntoTheModifierByte()
    {
        var router = new KeyRouter();

        router.OnKey(LShift, true);
        Assert.Equal(0x02, router.Report[0]);
        router.OnKey(A, true);
        Assert.Equal(new byte[] { 0x02, 0, 0x04, 0, 0, 0, 0, 0 }, router.Report);
    }

    [Fact]
    public void WindowsKeyIsForwardedAsCommand()
    {
        var router = new KeyRouter();

        Assert.Equal(KeyAction.Forward, router.OnKey(LWin, true));
        Assert.Equal((byte)KeyModifiers.LeftGui, router.Report[0]);
    }

    [Fact]
    public void AutoRepeatDoesNotRepeatTheUsage()
    {
        var router = new KeyRouter();

        router.OnKey(A, true);
        router.OnKey(A, true);
        router.OnKey(A, true);

        Assert.Equal(new byte[] { 0, 0, 0x04, 0, 0, 0, 0, 0 }, router.Report);
    }

    [Fact]
    public void ReleaseOfAKeyPressedBeforeCaptureGoesToWindows()
    {
        var router = new KeyRouter();

        Assert.Equal(KeyAction.PassThrough, router.OnKey(A, false));
        Assert.Equal(Released, router.Report);
    }

    [Fact]
    public void KeyHeldByWindowsAtStartStaysWithWindowsUntilReleased()
    {
        // e.g. Ctrl+Alt still down from the hotkey that started control
        var router = new KeyRouter(new[] { LCtrl });

        Assert.Equal(KeyAction.PassThrough, router.OnKey(LCtrl, true)); // auto-repeat
        Assert.Equal(KeyAction.PassThrough, router.OnKey(LCtrl, false));
        Assert.Equal(KeyAction.Forward, router.OnKey(LCtrl, true));
        Assert.Equal((byte)KeyModifiers.LeftControl, router.Report[0]);
    }

    [Fact]
    public void KeysHeldByWindowsAreNotSentAsModifiers()
    {
        var router = new KeyRouter(new[] { LShift });

        router.OnKey(A, true);

        Assert.Equal(new byte[] { 0, 0, 0x04, 0, 0, 0, 0, 0 }, router.Report);
    }

    [Fact]
    public void KeysWithoutAUsageStayWithWindows()
    {
        var router = new KeyRouter();

        Assert.Equal(KeyAction.PassThrough, router.OnKey(VolumeUp, true));
        Assert.Equal(KeyAction.PassThrough, router.OnKey(VolumeUp, true));
        Assert.Equal(KeyAction.PassThrough, router.OnKey(VolumeUp, false));
        Assert.Equal(Released, router.Report);
    }

    [Fact]
    public void CtrlAltQStopsAndReleasesEverything()
    {
        var router = new KeyRouter();
        router.OnKey(LShift, true);
        router.OnKey(LCtrl, true);
        router.OnKey(LAlt, true);

        Assert.Equal(KeyAction.Stop, router.OnKey(Q, true));
        Assert.Equal(Released, router.Report);
    }

    [Fact]
    public void CtrlAltQStopsWhenWindowsStillHoldsCtrlAndAlt()
    {
        var router = new KeyRouter(new[] { LCtrl, LAlt });

        Assert.Equal(KeyAction.Stop, router.OnKey(Q, true));
    }

    [Fact]
    public void AltGrQStops()
    {
        // AltGr arrives as left Ctrl plus right Alt
        var router = new KeyRouter();
        router.OnKey(LCtrl, true);
        router.OnKey(RAlt, true);

        Assert.Equal(KeyAction.Stop, router.OnKey(Q, true));
    }

    [Fact]
    public void QWithOnlyCtrlIsForwarded()
    {
        var router = new KeyRouter();
        router.OnKey(RCtrl, true);

        Assert.Equal(KeyAction.Forward, router.OnKey(Q, true));
        Assert.Equal(new byte[] { 0x10, 0, 0x14, 0, 0, 0, 0, 0 }, router.Report);
    }

    [Fact]
    public void AltTabIsHandedToWindowsWithTheHeldModifiers()
    {
        var router = new KeyRouter();
        router.OnKey(LShift, true);
        router.OnKey(LAlt, true);

        Assert.Equal(KeyAction.HandToWindows, router.OnKey(Tab, true));
        Assert.Equal(new[] { LShift, LAlt, Tab }, router.HandOver);
        Assert.Equal(Released, router.Report);
    }

    [Fact]
    public void AltEnterIsHandedToWindows()
    {
        var router = new KeyRouter();
        router.OnKey(LAlt, true);

        Assert.Equal(KeyAction.HandToWindows, router.OnKey(Enter, true));
        Assert.Equal(new[] { LAlt, Enter }, router.HandOver);
    }

    [Fact]
    public void AltTabWhileWindowsHoldsAltHandsOverOnlyTheKey()
    {
        var router = new KeyRouter(new[] { LAlt });

        Assert.Equal(KeyAction.HandToWindows, router.OnKey(Tab, true));
        Assert.Equal(new[] { Tab }, router.HandOver);
    }

    [Fact]
    public void TabAndEnterWithoutAltAreForwarded()
    {
        var router = new KeyRouter();

        Assert.Equal(KeyAction.Forward, router.OnKey(Tab, true));
        Assert.Equal(KeyAction.Forward, router.OnKey(Enter, true));
        Assert.Equal(new byte[] { 0, 0, 0x2B, 0x28, 0, 0, 0, 0 }, router.Report);
    }

    [Fact]
    public void ReportCarriesTheFirstSixKeysAndTheSeventhMovesUp()
    {
        var router = new KeyRouter();
        for (var vk = A; vk < A + 7; vk++) router.OnKey(vk, true);

        Assert.Equal(new byte[] { 0, 0, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 }, router.Report);

        router.OnKey(B, false);
        Assert.Equal(new byte[] { 0, 0, 0x04, 0x06, 0x07, 0x08, 0x09, 0x0A }, router.Report);
    }

    [Fact]
    public void NumpadIsResolvedFromThePhysicalKey()
    {
        var router = new KeyRouter();

        router.OnKey(0x24, true, 0x47, false);

        Assert.Equal(0x5F, router.Report[2]);
        Assert.Equal(KeyAction.Forward, router.OnKey(0x24, false, 0x47, false));
        Assert.Equal(Released, router.Report);
    }

    [Fact]
    public void AfterStopNothingIsHeldOnTheDevice()
    {
        var router = new KeyRouter();
        router.OnKey(LCtrl, true);
        router.OnKey(LAlt, true);
        router.OnKey(Q, true);

        // the releases arrive after control ended; Windows never saw the presses, which is harmless
        Assert.Equal(KeyAction.PassThrough, router.OnKey(LCtrl, false));
        Assert.Equal(KeyAction.PassThrough, router.OnKey(LAlt, false));
        Assert.Equal(Released, router.Report);
    }
}
