using MacExtend.Core.Bluetooth;

namespace MacExtend.Core.Tests.Bluetooth;

public class ReportQueueTests
{
    private static readonly byte[] KeyA = HidReports.Keyboard(KeyModifiers.None, new byte[] { 0x04 });
    private static readonly byte[] KeyB = HidReports.Keyboard(KeyModifiers.None, new byte[] { 0x05 });

    [Fact]
    public void MotionIsCoalesced()
    {
        var queue = new ReportQueue();
        queue.EnqueueMouse(MouseButtons.None, 1, 2, 0);
        queue.EnqueueMouse(MouseButtons.None, 3, 4, 1);

        Assert.False(queue.TryDequeueOrdered(out _));
        Assert.True(queue.TryTakeMouse(out var mouse));
        Assert.Equal((4, 6, 1), (mouse.Dx, mouse.Dy, mouse.Wheel));
        Assert.False(queue.HasMouse);
    }

    [Fact]
    public void EveryKeyboardReportIsKeptInOrder()
    {
        var queue = new ReportQueue();
        queue.EnqueueKeyboard(KeyA);
        queue.EnqueueKeyboard(KeyB);
        queue.EnqueueKeyboard(HidReports.KeyboardRelease());

        Assert.True(queue.TryDequeueOrdered(out var first));
        Assert.True(queue.TryDequeueOrdered(out var second));
        Assert.True(queue.TryDequeueOrdered(out var third));
        Assert.Equal(KeyA, first.Keyboard);
        Assert.Equal(KeyB, second.Keyboard);
        Assert.Equal(HidReports.KeyboardRelease(), third.Keyboard);
        Assert.False(queue.TryDequeueOrdered(out _));
    }

    [Fact]
    public void AQuickClickIsNotCoalescedAway()
    {
        var queue = new ReportQueue();
        queue.EnqueueMouse(MouseButtons.Left, 0, 0, 0);
        queue.EnqueueMouse(MouseButtons.None, 0, 0, 0);

        Assert.True(queue.TryDequeueOrdered(out var down));
        Assert.False(down.IsKeyboard);
        Assert.Equal(MouseButtons.Left, down.Buttons);
        Assert.True(queue.TryTakeMouse(out var up));
        Assert.Equal(MouseButtons.None, up.Buttons);
    }

    [Fact]
    public void DragKeepsCoalescingWhileTheButtonIsHeld()
    {
        var queue = new ReportQueue();
        queue.EnqueueMouse(MouseButtons.Left, 0, 0, 0);
        queue.EnqueueMouse(MouseButtons.Left, 5, 0, 0);
        queue.EnqueueMouse(MouseButtons.Left, 5, -1, 0);

        Assert.False(queue.TryDequeueOrdered(out _));
        Assert.True(queue.TryTakeMouse(out var drag));
        Assert.Equal((MouseButtons.Left, 10, -1), (drag.Buttons, drag.Dx, drag.Dy));
    }

    [Fact]
    public void KeyboardAfterMotionKeepsTheOrder()
    {
        var queue = new ReportQueue();
        queue.EnqueueMouse(MouseButtons.Left, 5, 0, 0);
        queue.EnqueueKeyboard(KeyA);

        Assert.True(queue.TryDequeueOrdered(out var mouse));
        Assert.False(mouse.IsKeyboard);
        Assert.Equal(5, mouse.Dx);
        Assert.True(queue.TryDequeueOrdered(out var key));
        Assert.True(key.IsKeyboard);
        Assert.False(queue.HasMouse);
    }

    [Fact]
    public void MotionWaitsForTheOrderedReports()
    {
        var queue = new ReportQueue();
        queue.EnqueueKeyboard(KeyA);
        queue.EnqueueMouse(MouseButtons.None, 1, 1, 0);

        Assert.False(queue.TryTakeMouse(out _));
        Assert.True(queue.TryDequeueOrdered(out _));
        Assert.True(queue.TryTakeMouse(out var mouse));
        Assert.Equal(1, mouse.Dx);
    }

    [Fact]
    public void ClearDropsEverything()
    {
        var queue = new ReportQueue();
        queue.EnqueueKeyboard(KeyA);
        queue.EnqueueMouse(MouseButtons.None, 1, 1, 0);

        queue.Clear();

        Assert.False(queue.TryDequeueOrdered(out _));
        Assert.False(queue.TryTakeMouse(out _));
    }
}
