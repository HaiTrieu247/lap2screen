using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SetupGateTests
{
    private readonly object _first = new object();
    private readonly object _second = new object();

    [Fact]
    public void OneSetupActionAtATimeWhicheverWindowAsks()
    {
        var gate = new SetupGate();

        Assert.True(gate.TryEnter(_first, SetupStepId.Components, "Windows sẽ hỏi quyền quản trị một lần."));
        Assert.False(gate.TryEnter(_second, SetupStepId.Components, "again"));
        Assert.False(gate.TryEnter(_first, SetupStepId.DiskImage, "again"));

        Assert.True(gate.IsHeld);
        Assert.Same(_first, gate.Owner);
        Assert.Equal(SetupStepId.Components, gate.Step);
        Assert.Equal("Windows sẽ hỏi quyền quản trị một lần.", gate.Message);
    }

    [Fact]
    public void OnlyTheOwnerChangesTheStepTheMessageOrLetsGo()
    {
        var gate = new SetupGate();
        gate.TryEnter(_first, null, "Đang kiểm tra...");

        gate.SetStep(_second, SetupStepId.Wifi);
        gate.SetMessage(_second, "other");
        gate.Exit(_second);
        Assert.True(gate.IsHeld);
        Assert.Null(gate.Step);
        Assert.Equal("Đang kiểm tra...", gate.Message);

        gate.SetStep(_first, SetupStepId.Trust);
        gate.SetMessage(_first, "Đang đọc thiết bị.");
        Assert.Equal(SetupStepId.Trust, gate.Step);
        Assert.Equal("Đang đọc thiết bị.", gate.Message);

        gate.Exit(_first);
        Assert.False(gate.IsHeld);
        Assert.Null(gate.Owner);
        Assert.Null(gate.Step);
        Assert.Equal("", gate.Message);
        Assert.True(gate.TryEnter(_second, SetupStepId.DiskImage, null));
        Assert.Equal("", gate.Message);
    }

    [Fact]
    public void EveryChangeIsAnnouncedAndARefusalIsNot()
    {
        var gate = new SetupGate();
        var changes = 0;
        gate.Changed += () => changes++;

        gate.TryEnter(_first, SetupStepId.Wifi, "a");
        gate.TryEnter(_second, SetupStepId.Wifi, "b");
        gate.SetMessage(_second, "c");
        gate.SetMessage(_first, "d");
        gate.SetStep(_first, null);
        gate.Exit(_second);
        gate.Exit(_first);
        gate.Exit(_first);

        Assert.Equal(4, changes);
    }

    [Fact]
    public void TheGateIsHeldAgainWhileItsReleaseIsAnnounced()
    {
        // a waiting window refreshes when the gate is let go; entering from that handler must work
        var gate = new SetupGate();
        var reentered = false;
        gate.TryEnter(_first, null, "a");
        gate.Changed += () =>
        {
            if (!gate.IsHeld) reentered = gate.TryEnter(_second, null, "b");
        };

        gate.Exit(_first);

        Assert.True(reentered);
        Assert.Same(_second, gate.Owner);
    }
}
