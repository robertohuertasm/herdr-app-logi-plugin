namespace HerdrApp.Tests;

public class NavigationTests
{
    [Theory]
    [InlineData("p1", 1, "p2")]
    [InlineData("p1", -1, "p2")]
    [InlineData("missing", 1, null)]
    public void NeighborWrapsOnlyWhenCurrentExists(string current, int direction, string? expected) =>
        Assert.Equal(expected, Navigation.Neighbor(["p1", "p2"], current, direction));

    [Theory]
    [InlineData("cycle", "p2")]
    [InlineData("pane-next", "p2")]
    [InlineData("pane-prev", "p2")]
    public async Task PaneNavigationStaysInTheFocusedTab(string id, string expected)
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        await new Navigation(app.Client()).ExecuteAsync(id);
        Assert.Equal(new[] { ("pane.focus", $$"""{"pane":"{{expected}}"}""") }, app.Commands);
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
    [InlineData("My agent", "api", "claude", "My agent")]
    [InlineData(null, "api", "claude", "api")]
    [InlineData(null, "", "claude", "claude")]
    [InlineData(null, "", null, "Recent 3")]
    public void AgentLabelPrefersNameThenSpaceThenApp(string? name, string space, string? app, string expected) =>
        Assert.Equal(expected, FixedSlots.AgentLabel(new Agent { Name = name, SpaceName = space, App = app }, 3));
}
