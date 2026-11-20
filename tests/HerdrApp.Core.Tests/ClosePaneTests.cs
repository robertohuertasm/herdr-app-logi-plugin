namespace HerdrApp.Tests;

public class ClosePaneTests
{
    [Fact]
    public void GestureRequiresHoldThenAnotherPressAndRelease()
    {
        var gesture = new ClosePaneGesture();
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, false));
        Assert.Equal(ClosePaneGestureAction.Tap, gesture.Process(ClosePaneButtonEvent.Release, false));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, false));
        Assert.Equal(ClosePaneGestureAction.Arm, gesture.Process(ClosePaneButtonEvent.LongPress, false));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.RepeatPress, true));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, true));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, true));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, true));
        Assert.Equal(ClosePaneGestureAction.Confirm, gesture.Process(ClosePaneButtonEvent.Release, true));
    }

    [Theory]
    [InlineData("p1", "a1", true)]
    [InlineData("p2", "a1", false)]
    [InlineData("p1", "replacement", false)]
    public async Task ConfirmationOnlyClosesTheSameFocusedPane(string focused, string terminal, bool expected)
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        var client = app.Client();
        var confirmation = new ClosePaneConfirmation(client);
        Assert.True(confirmation.Arm(await client.GetSnapshotAsync()));
        Assert.True(confirmation.IsArmed);
        app.Status = Fixtures.Status.Replace("\"focused_pane\":\"p1\"", $"\"focused_pane\":\"{focused}\"");
        app.Ls = Fixtures.Ls.Replace("\"terminal\":\"a1\"", $"\"terminal\":\"{terminal}\"");
        Assert.Equal(expected, await confirmation.ConfirmAsync());
        Assert.Equal(expected ? new[] { ("pane.close", """{"pane":"p1"}""") } : [], app.Commands);
        Assert.False(await confirmation.ConfirmAsync());
    }

    [Fact]
    public void NothingToArmWithoutAFocusedPane()
    {
        var confirmation = new ClosePaneConfirmation(new HerdrAppClient("/nonexistent", new NoWindow()));
        Assert.False(confirmation.Arm(Snapshot.Empty));
    }
}
