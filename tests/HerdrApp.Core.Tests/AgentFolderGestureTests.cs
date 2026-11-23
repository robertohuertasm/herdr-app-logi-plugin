namespace HerdrApp.Tests;

public class AgentFolderGestureTests
{
    [Fact]
    public void OuterParentHoldOpensTheDetailAndItsReleaseDoesNothing()
    {
        var gesture = new AgentFolderGesture();
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, true, true, false));
        Assert.Equal(AgentFolderGestureAction.OpenSubAgents, gesture.Process(ClosePaneButtonEvent.LongPress, true, true, false));
        // The same physical press ends after the parent is redrawn in detail.
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, false, true, false));
    }

    [Fact]
    public void DetailKeyNeedsAHoldAndASeparateConfirmationTap()
    {
        var gesture = new AgentFolderGesture();
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, false, true, false));
        Assert.Equal(AgentFolderGestureAction.ArmClose, gesture.Process(ClosePaneButtonEvent.LongPress, false, true, false));
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.RepeatPress, false, true, true));
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, false, true, true));
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, false, true, true));
        Assert.Equal(AgentFolderGestureAction.ConfirmClose, gesture.Process(ClosePaneButtonEvent.Release, false, true, true));
    }

    [Fact]
    public void DoubleTapWithoutAHoldOnlyFocuses()
    {
        var gesture = new AgentFolderGesture();
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, false, true, false));
            Assert.Equal(AgentFolderGestureAction.Tap, gesture.Process(ClosePaneButtonEvent.Release, false, true, false));
        }
    }

    [Fact]
    public void HoldingTheConfirmationAgainDoesNotClose()
    {
        var gesture = new AgentFolderGesture();
        gesture.Process(ClosePaneButtonEvent.Press, false, true, true);
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.LongPress, false, true, true));
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, false, true, true));
    }

    [Fact]
    public void ChildWithoutAPaneCanBePeekedButCannotArmAPaneClose()
    {
        var gesture = new AgentFolderGesture();
        gesture.Process(ClosePaneButtonEvent.Press, false, false, false);
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.LongPress, false, false, false));
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, false, false, false));
        gesture.Process(ClosePaneButtonEvent.Press, false, false, false);
        Assert.Equal(AgentFolderGestureAction.Tap, gesture.Process(ClosePaneButtonEvent.Release, false, false, false));
    }

    [Fact]
    public void LeavingTheViewDiscardsThePendingPress()
    {
        var gesture = new AgentFolderGesture();
        gesture.Process(ClosePaneButtonEvent.Press, false, true, true);
        gesture.Cancel();
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, false, true, true));
    }

    [Theory]
    [InlineData("a1", "p1")]
    [InlineData("a2", "p4")]
    public async Task ConfirmedParentOrChildCloseTargetsOnlyItsOwnPane(string key, string pane)
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        var client = app.Client();
        var snapshot = await client.GetSnapshotAsync();
        var agent = snapshot.Agents.Single(a => a.Key == key);
        var gesture = new AgentFolderGesture();
        var confirmation = new AgentCloseConfirmation();
        gesture.Process(ClosePaneButtonEvent.Press, false, true, false);
        Assert.Equal(AgentFolderGestureAction.ArmClose, gesture.Process(ClosePaneButtonEvent.LongPress, false, true, false));
        Assert.True(confirmation.Arm(agent));
        Assert.Equal(AgentFolderGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, false, true, true));
        Assert.Empty(app.Commands);
        gesture.Process(ClosePaneButtonEvent.Press, false, true, true);
        Assert.Equal(AgentFolderGestureAction.ConfirmClose, gesture.Process(ClosePaneButtonEvent.Release, false, true, true));
        Assert.True(await new CloseAgent(client).CloseIfStillPresentAsync(confirmation.Take()!));
        Assert.Equal(new[] { ("pane.close", $$"""{"pane":"{{pane}}"}""") }, app.Commands);
    }
}
