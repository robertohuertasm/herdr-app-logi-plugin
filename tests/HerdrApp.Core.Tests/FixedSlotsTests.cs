namespace HerdrApp.Tests;

public class FixedSlotsTests
{
    [Fact]
    public void RecentAndSpaceSlotsHandleMissingEntries()
    {
        var first = new Agent { TerminalId = "a1", AgentStatus = "blocked", App = "codex" };
        var second = new Agent { TerminalId = "a2", AgentStatus = "working", App = "claude" };
        var space = new Space { SpaceId = "s2", Index = 2, Name = "api" };
        var snapshot = new Snapshot { Recent = [first, second], Spaces = [space] };
        Assert.Same(first, FixedSlots.Recent(snapshot, 1));
        Assert.Same(second, FixedSlots.Recent(snapshot, 2));
        Assert.Null(FixedSlots.Recent(snapshot, 3));
        Assert.Null(FixedSlots.Recent(snapshot, 10));
        Assert.Same(space, FixedSlots.Space(snapshot, 2));
        Assert.Null(FixedSlots.Space(snapshot, 1));
        Assert.Equal("codex", FixedSlots.AgentLabel(first, 1));
        Assert.Equal("api", FixedSlots.SpaceLabel(space, 2));
        Assert.Equal("Space 4", FixedSlots.SpaceLabel(new Space(), 4));
    }

    [Fact]
    public async Task SpacePressShowsTheSpaceThenCyclesItsActiveTab()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = """{"space":"s2","tab":"t3","focused_pane":"p4"}""" };
        app.OnDo = (name, args) =>
        {
            if (name == "space.select") app.Status = Fixtures.Status;
            return "null";
        };
        var actions = new FixedSlotActions(app.Client());
        await actions.FocusSpaceAsync(1);
        await actions.FocusSpaceAsync(1);
        Assert.Equal(new[] { ("space.select", """{"id":"s1"}"""), ("pane.focus", """{"pane":"p2"}""") }, app.Commands);
    }

    [Fact]
    public async Task EmptySpaceSlotDoesNothing()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        await new FixedSlotActions(app.Client()).FocusSpaceAsync(7);
        Assert.Empty(app.Commands);
    }
}
