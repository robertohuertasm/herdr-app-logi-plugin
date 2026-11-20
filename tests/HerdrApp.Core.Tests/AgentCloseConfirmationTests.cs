namespace HerdrApp.Tests;

public class AgentCloseConfirmationTests
{
    [Fact]
    public void HeldAgentStaysTargetedAfterReorder()
    {
        var confirmation = new AgentCloseConfirmation();
        var snapshot = new Snapshot { Recent = [Agent("a1", "p1"), Agent("a2", "p2")] };
        Assert.True(confirmation.Arm(FixedSlots.Recent(snapshot, 1)));
        // Slot 1 now shows another agent; the pinned target must not follow it.
        snapshot = snapshot with { Recent = [Agent("a2", "p2"), Agent("a1", "p1")] };
        Assert.Equal("a2", FixedSlots.Recent(snapshot, 1)?.Key);
        Assert.Equal(new CloseTarget("a1", "p1", "s1", "codex"), confirmation.Take());
        Assert.Null(confirmation.Take());
    }

    [Fact]
    public void ExpiredCancelledOrUnidentifiedAgentIsNeverTargeted()
    {
        var clock = new TestClock();
        var confirmation = new AgentCloseConfirmation(clock);
        Assert.True(confirmation.Arm(Agent("a1", "p1")));
        clock.Advance(ClosePaneConfirmation.Window);
        Assert.False(confirmation.IsArmed);
        Assert.True(confirmation.Expire());
        Assert.Null(confirmation.Take());
        Assert.True(confirmation.Arm(Agent("a1", "p1")));
        confirmation.Cancel();
        Assert.Null(confirmation.Take());
        Assert.False(confirmation.Arm(null));
        Assert.False(confirmation.Arm(Agent("", "p1")));
    }

    private static Agent Agent(string terminal, string pane) =>
        new() { TerminalId = terminal, PaneId = pane, SpaceId = "s1", App = "codex" };

    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => this.ticks;
        public void Advance(TimeSpan duration) => this.ticks += duration.Ticks;
    }
}
