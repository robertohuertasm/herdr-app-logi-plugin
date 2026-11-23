namespace HerdrApp.Tests;

public class NavigationTests
{
    [Theory]
    [InlineData("p1", 1, "p2")]
    [InlineData("p1", -1, "p2")]
    [InlineData("missing", 1, null)]
    public void NeighborWrapsOnlyWhenCurrentExists(string current, int direction, string? expected) =>
        Assert.Equal(expected, Navigation.Neighbor(["p1", "p2"], current, direction));

    [Fact]
    public async Task CyclePanesStaysInTheFocusedTab()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        await new Navigation(app.Client()).ExecuteAsync("cycle");
        Assert.Equal(new[] { ("pane.focus", """{"pane":"p2"}""") }, app.Commands);
    }

    [Fact]
    public async Task SinglePaneTabHasNothingToCycle()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = """{"space":"s2","tab":"t3","focused_pane":"p4"}""" };
        await new Navigation(app.Client()).ExecuteAsync("cycle");
        Assert.Empty(app.Commands);
    }

    [Theory]
    [InlineData("zoom", "pane.zoom", "{}")]
    [InlineData("go-to", "mod.open", """{"name":"picker"}""")]
    [InlineData("cycle-tabs", "tab.next", "{}")]
    [InlineData("tab-next", "tab.next", "{}")]
    [InlineData("tab-prev", "tab.prev", "{}")]
    [InlineData("tab-new", "tab.new", "{}")]
    [InlineData("space-next", "space.next", "{}")]
    [InlineData("space-prev", "space.prev", "{}")]
    [InlineData("split-right", "pane.split", """{"direction":"right"}""")]
    [InlineData("split-down", "pane.split", """{"direction":"down"}""")]
    [InlineData("pane-left", "pane.go", """{"direction":"left"}""")]
    [InlineData("pane-up", "pane.go", """{"direction":"up"}""")]
    [InlineData("pane-down", "pane.go", """{"direction":"down"}""")]
    [InlineData("pane-right", "pane.go", """{"direction":"right"}""")]
    [InlineData("sidebar", "layout.toggle", """{"id":"sidebar"}""")]
    public async Task ShortcutsRunTheAppsOwnCommands(string id, string command, string args)
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        await new Navigation(app.Client()).ExecuteAsync(id);
        Assert.Equal(new[] { (command, args) }, app.Commands);
    }

    [Theory]
    [InlineData("s1", "")]
    [InlineData("vps/s3", "vps")]
    public async Task NewSpaceFollowsTheFocusedSpacesMachine(string focused, string machine)
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = $$"""{"space":"{{focused}}"}""" };
        await new Navigation(app.Client()).ExecuteAsync("space-new");
        Assert.Equal(new[] { ("space.new", $$"""{"machine":"{{machine}}"}""") }, app.Commands);
    }

    [Fact]
    public async Task ClosePaneAndUnknownShortcutsAreRefused()
    {
        using var app = new FakeApp();
        var navigation = new Navigation(app.Client());
        await Assert.ThrowsAsync<InvalidOperationException>(() => navigation.ExecuteAsync("close-pane"));
        await Assert.ThrowsAsync<ArgumentException>(() => navigation.ExecuteAsync("nope"));
        Assert.Empty(app.Commands);
    }

    [Theory]
    [InlineData("My agent", "PR 9750", "Fix login", "My agent")]
    [InlineData(null, "PR 9750", "Fix login", "PR 9750")]
    [InlineData(null, "2", "Fix login", "Fix login")]
    [InlineData(null, "Claude Code", "Fix login", "Fix login")]
    [InlineData(null, "2", null, "dd-source")]
    public void AgentLabelPrefersNameThenTabThenTitleThenSpace(string? name, string tab, string? title, string expected) =>
        Assert.Equal(expected, FixedSlots.AgentLabel(
            new Agent { Name = name, TabName = tab, Title = title, SpaceName = "dd-source", App = "claude-code" }, 3));

    [Fact]
    public void AgentLabelFallsBackToTheAppAndThenTheSlot()
    {
        Assert.Equal("claude", FixedSlots.AgentLabel(new Agent { App = "claude" }, 3));
        Assert.Equal("Recent 3", FixedSlots.AgentLabel(new Agent(), 3));
    }
}
